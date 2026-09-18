# LeoClassroom backend development rules

These instructions apply to the backend solution in this directory. They are the LeoWebApi template rules, with
the additions this application needs. Preserve the existing layered minimal-API architecture and its explicit
control flow.

## Architecture and dependency direction

- `LeoClassroom` is the transport/composition layer: startup, endpoint groups, HTTP DTOs, authentication and
  authorization wiring, explicit request validation, and HTTP result mapping.
- `LeoClassroom.Services` contains business workflows and logic - the services themselves at the project root, and
  the integrations (`Forgejo/`, `Moodle/`, `Ldap/`, `Notifications/`, `Download/`, `Provisioning/`, `Audit/`,
  `Scheduling/`, `Security/`) in their own folders. Services depend on persistence abstractions and return domain
  outcomes; they do not know HTTP.
  (This is the template's `<Name>.Services` project; it was called `.Core` before the template realignment.)
- `LeoClassroom.Persistence` contains EF entities, `DatabaseContext`, repositories, and the unit of work/transaction
  provider. Repositories query or stage entity changes; the unit of work saves them.
- `LeoClassroom.Shared` contains small cross-layer primitives only. Do not turn it into a dumping ground or make it
  depend on higher layers.
- Dependencies flow inward from the host through services to persistence/shared code. Endpoints may use persistence
  entities for explicit DTO mapping and `ITransactionProvider` for their transaction boundary, as the template does,
  but must not query `DatabaseContext` or repositories directly. Do not introduce reverse references.
- `LeoClassroom.Test` is the isolated unit-test project. `LeoClassroom.TestInt` is the full HTTP/database
  integration-test project.

## HTTP and business flow

- Put each resource in its own static endpoint class and route group under `Endpoints/`. Add its mapping explicitly
  in `Setup.MapApplicationEndpoints`; do not use reflection or controllers.
- Endpoint methods own binding, authorization, validation, transaction boundaries, and conversion between service
  outcomes and typed HTTP results. Keep business rules in services.
- Keep API request/response DTOs out of persistence and do not expose EF entities as the HTTP contract. Map
  deliberately between them.
- Declare every expected response using `TypedResults` and `Results<...>`, with resource-specific status codes and
  RFC 9457 validation/problem details. Answer 403 through `ApiResults.Forbidden()` and an upstream failure through
  `ApiResults.BadGateway(...)`, so every error has the same shape.
- The outcomes that recur across resources (404/403, commit-then-204, commit-then-200) are translated by the
  `Outcomes` extensions in `Util/`. A `Results<...>` union does not widen, so an endpoint that can also answer 400
  uses the `...OrInvalid` variant.
- Use FluentValidation for request objects, normally with the nested `Request.Validator` pattern. Invoke validation
  explicitly in the endpoint before starting a transaction: create the validator and call `validator.Check(request)`,
  which returns the `ValidationProblem` to return or null when the request is valid. Use `Validation.Failure` for
  simple checks.
- Validators are database-less and compare against constants, configuration, or the clock. A validator needing extra
  information takes it through its constructor, and the endpoint passes it when creating the validator. Validators
  are not registered in DI, discovered by reflection, or run from an endpoint filter - an endpoint that can answer
  400 must declare `ValidationProblem` in its own `Results<...>` union and return it itself.
- Express "this id cannot address a resource" with a route constraint (`{id:long:min(1)}`) rather than a check in
  the handler.
- Services must represent expected alternatives with `OneOf<...>` (`Success`, `NotFound`, `Forbidden`, or small
  domain-specific result types). Do not use exceptions, `null`, booleans, or magic values for multi-outcome business
  operations. Endpoints must exhaustively translate outcomes with `Match`. See **Reading a `OneOf` outcome** below,
  which applies to every layer, not only to endpoints.
- Exceptions are for unexpected failures and are translated centrally by `GlobalExceptionHandler`; never leak their
  details to clients. The same applies to anything persisted for a client to read back - a background job records a
  generic failure and logs the exception.

## Reading a `OneOf` outcome

- **Never read a union through `IsT0`/`IsT1`/`AsT2` or any other positional accessor.** They are the one thing in
  this codebase that keeps compiling after a case is inserted into a union and then quietly means a different case,
  which is how a failure ends up being handled as a success. There is no exception to this - not in services, not in
  endpoints, not in workers, not in tests.
- Read every outcome with `Match` (it produces a value) or `Switch` (it only has side effects). Both force every
  case to be named, so adding a case to a union turns into a compile error at each place that has to decide about it.
- A branch that needs no work of its own still gets written out: `success => { }` in a `Switch`, or
  `notFound => ValueTask.FromResult<...>(notFound)` next to a branch that continues the workflow asynchronously.
  That verbosity is the point - it is the list of cases somebody has actually thought about.
- For the very common "propagate the failure, otherwise carry on" guard over a two-case
  `OneOf<value, failure>`, use `Outcome.Failure` from `LeoClassroom.Services.Util`, which is that `Match` written
  once - `if (clone.Failure is { } failed)` and return `failed` from the block. It keeps a sequence of fallible
  steps flat instead of nesting one match lambda inside the next.
- When the success value *is* needed, match and hand the value to a named private method rather than nesting
  lambdas - `ReconciliationService`, `RepoProvisioningService` and `SubmissionReviewService` are the worked examples.
- Recurring endpoint translations live in `Util/Outcomes`; recurring service-level gates live next to the service
  that needs them (`MoodleLinkService.AsCourseTeacherAsync`). Add to those rather than writing the same match out
  again.
- In tests, assert the case by its type through `ShouldBe<TCase>()` (`LeoClassroom.Test/OutcomeAssertions.cs`),
  never `result.IsT1.Should().BeTrue()`.

## Authentication and authorization

- Every route group states its policy with `.RequireAuthorization(AuthPolicies.…)`. A group that is genuinely public
  says `.AllowAnonymous()` and explains why. The fallback policy requires an authenticated user, so a group that
  states nothing is closed rather than open - do not rely on that, state the intent.
- The caller's identity is the `preferred_username` claim (the IF number), read through `AuthClaims.ReadStudentId`.
  Never fall back to `ClaimTypes.Name`: inbound claim mapping is off precisely so that Keycloak's display name
  cannot end up being treated as a user id, and every ownership check compares against this value.
- A token that asserts no role must not rewrite a stored role. `AuthClaims.PrimaryRole` returns null in that case
  and `UserProvisioningService` keeps what the user already has.
- Provisioning is a once-per-user event, so `IUserProvisioningCache` holds the stored state of every known caller
  with no expiry: it is filled from the user table at boot and grows as new callers appear. A request costs a
  dictionary lookup, not a database round-trip.
- It caches the refusal as well as the pass. A disabled account presenting a still-valid token must be turned away
  from memory, or repeated attempts by one become database load. Only a genuinely unknown identity reaches
  provisioning. The audit record for a refusal is therefore throttled per account - the application log has every
  attempt, the audit table has at most one per window.
- The cache entry is a fingerprint of exactly the columns provisioning writes, so a caller whose name, mail address
  or role changed reconciles on their next request and is free again afterwards. If you make provisioning write a
  new column, add it to that fingerprint.
- **Anything that changes `User.State` must call `Clear()`** - a soft-deleted account otherwise keeps working
  indefinitely. The LDAP sync, which is the only thing that soft-deletes or reactivates, already does.
- The cache assumes a single backend instance, which this application already requires anyway (Quartz runs
  non-clustered against an in-memory job store). Adding a replica means giving the cache a real invalidation
  channel first.
- Authorization decisions about *data* (owns this course, teaches this assignment) belong in the services, not in
  policies. Return `Forbidden` from the service and let the endpoint translate it.
- `UserProvisioningMiddleware` runs *after* `UseAuthorization`, so a request that is about to be refused never
  causes a provisioning write. Keep it there.

## Deletion

- Soft delete (`UserState.SoftDeleted`) is what the LDAP sync applies when someone leaves the directory. The row
  stays so that assignments, acceptances and audit history keep resolving, and the sync reverses it if they return.
- Hard delete (`IUserDeletionService`) is a **cascade**: the user takes their own submissions, and also the rosters,
  courses and assignments they own together with every other student's submissions inside those. Every destructive
  entry point therefore has a preview counterpart that reports the blast radius and changes nothing, and the bulk
  purge additionally refuses to run without `confirm: true`. Keep both properties if you add another one.
- **`IDeletionCascade` is the only place that deletes a course, an assignment or an acceptance.** Do not delete
  those rows anywhere else and do not lean on the database's cascade for them: a database cascade removes rows,
  and each of those rows names something in Forgejo that becomes unreachable the moment the row is gone. All four
  delete paths - assignment, auto-delete, course, user - go through it.
- The order inside the cascade is fixed and runs from the leaves upward: repositories before the acceptances that
  name them, acceptances before their assignment, assignments before their course, and the Forgejo organisation
  only once it holds no repositories, which is also what Forgejo itself requires. Rows are deleted even when the
  Forgejo call fails - an orphaned repository is recoverable, a half-deleted course is not - and the failure count
  comes back in `CascadeResult` and lands in the audit record.
- Pure many-to-many link rows are the one exception, and are left to the database: they mirror nothing outside it.
  Anything a user's deletion changes in Forgejo is explicit - their submission repositories, their membership of
  the teachers team of courses that outlive them, and their Forgejo account, which is deactivated because Forgejo
  has no user delete.
- The user-level closure is fixed by the foreign keys: a roster pulls in the courses that point at it, and a course
  pulls in its assignments. Widening the model means widening `BuildPlanAsync`.
- Retention is `RetentionPolicy`, not a request parameter: a departed user is kept until the end of the school year
  in which they disappeared (1 August) and then a further year, so a whole year group ages out together and nobody
  is deleted mid-year. Preview and purge share it, so they can never disagree about who is eligible.
- Only accounts the LDAP sync has actually retired are purge candidates: soft-deleted, with a non-null
  `LdapLastSeen` past the cutoff. A null `LdapLastSeen` means "never seen by LDAP", which is also true of someone
  provisioned from their claims a minute ago, so it never counts as inactive.
- Audit history survives deletion by design - `AuditEvent` stores the actor's IF number as text rather than as a
  foreign key. Do not turn it into a relationship.

## Persistence and transactions

- Every write endpoint must validate first, call `BeginTransactionAsync` immediately before the write workflow, and
  call `CommitAsync` only in its successful result branch. Read endpoints do not open a transaction.
- The endpoint owns the transaction. Services and repositories must never begin, commit, or hide a transaction. Do
  not rely on EF's implicit `SaveChanges` transaction for an endpoint write workflow.
- Leave unsuccessful result branches and exceptions uncommitted; scoped `UnitOfWork` disposal intentionally rolls
  them back. Use explicit rollback only when execution must continue within the same scope after the failed
  transaction.
- Services coordinate repositories and call `IUnitOfWork.SaveChangesAsync`; repositories must not save or return
  transport DTOs.
- Make tracking intent explicit. Reads are no-tracking by default; request tracking only when an entity will be
  modified or removed in the same unit of work.
- Keep mappings and constraints in `DatabaseContext`. Create a migration for every schema change (`ManageMigration.ps1`)
  and ensure integration tests apply it. `SchemaGuard` refuses to serve traffic against an unmigrated database.

## Background work and integrations

- Long-running work belongs in a `BackgroundService` or a Quartz job, never in an endpoint. An endpoint queues and
  answers 202.
- A worker resolves its own scope and its own `IUnitOfWork`; it never captures a scoped service.
- Anything reaching an external system (Forgejo, Moodle, LDAP, SMTP, `git`) returns a domain error type rather than
  throwing, and the endpoint answers 502 through `ApiResults.BadGateway`.
- Never put a credential on a command line - it is world-readable through `/proc`. `GitService` passes the service
  token through `GIT_CONFIG_*` environment variables for exactly this reason.
- Validate anything that reaches an external tool as an argument: clone URLs must be absolute http(s) (git's `ext::`
  transport executes commands), commit ids must be object names, archive entry paths must stay inside the extraction
  root.
- Secrets arrive as files under `/run/secrets`. `Setup.VerifyRequiredSecrets` fails the boot outside development
  when one is still blank; add new required secrets there.

## C# conventions

- Preserve nullable reference types, latest analyzers, warnings-as-errors, and the configured Leo analyzers. Fix
  warnings rather than suppressing them.
- Always use Allman braces: every block-opening brace is on its own line, including methods, properties, control
  flow, block lambdas, and local functions.
- Use file-scoped namespaces, four-space indentation, primary constructors where they improve clarity, and sealed
  types when inheritance is not intended.
- Do not use the null-forgiving operator (`!`) outside EF entity properties where framework materialization makes it
  strictly unavoidable. Prefer `required`, accurate nullable types, guards, pattern matching, or explicit exceptions.
  Never use `!` merely to silence analysis.
- Use NodaTime and injected `IClock` for domain time. Keep JSON behavior in `JsonConfig`, logging structured, and
  secrets/configuration out of source.
- Async methods use the `Async` suffix and do not block. Follow the established preference for explicit
  contract/result types and `var` only when the assigned type is obvious.

## Tests and coverage

- Every behavior change requires meaningful coverage of its success path plus relevant validation,
  not-found/forbidden/conflict, rollback, and edge paths. Do not count generated code, trivial accessors, or
  line-only execution as useful coverage.
- Unit tests belong in `LeoClassroom.Test`. Test pure logic and services in isolation, substituting `IUnitOfWork`,
  repositories, clocks, and other collaborators with NSubstitute. Unit tests must not start ASP.NET or access a real
  database, network, or filesystem.
- Integration tests belong in `LeoClassroom.TestInt`. Exercise the public API through `HttpClient` and
  `WebApplicationFactory` against PostgreSQL via Testcontainers; verify routing, binding, JSON, status codes,
  response bodies, persistence, and transaction behavior.
- Derive integration tests from `WebApiTestBase`, seed through `ImportSeedDataAsync`/`ModifyDatabaseContentAsync`,
  and rely on the fixture's per-test schema reset. Never make integration tests order-dependent. Set the caller with
  `AuthenticateAsTeacher`/`AsStudent`/`AsAdmin`/`Anonymous`.
- `EndpointGraphTests` is the exception: it runs without Postgres so that a route or authorization mistake is caught
  even where Docker is unavailable. Keep it free of database access.
- Use xUnit and AwesomeAssertions, keep tests deterministic, replace `IClock` through the test factory, and pass
  `TestContext.Current.CancellationToken` to cancellable calls.
- Assert a service outcome by naming its case - `result.ShouldBe<NotFound>()` - and never by its position in the
  union. See **Reading a `OneOf` outcome**.

Before finishing, run `dotnet build LeoClassroom.slnx` and the affected unit and integration tests (integration tests
require Docker and a current migration). Do not weaken production checks or tests to obtain a passing build.
