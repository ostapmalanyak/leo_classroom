# Offline regression checks

These checks compile the affected CLI and authorization/path helpers directly from production source,
using the installed .NET 10 SDK and Node 24. They require no third-party packages or network access.
HTTP responses are supplied by an in-memory handler. File fixtures remain under the ignored `obj/`
directory; the checks do not delete files or run publishing operations.

From this directory:

```sh
dotnet restore --configfile NuGet.Config --packages obj/packages
dotnet run --no-restore --disable-build-servers
node frontend.test.mjs
```

They cover exact LDAP OU matching, teacher/admin CLI authorization through `/api/me`, preservation of
existing ignore rules, private token-cache permissions, and repository path/symlink guards. They do not
replace the backend unit/integration suites or the Angular production build. Archive preflight and
provisioning transaction/worker regressions are covered in the backend test projects.
