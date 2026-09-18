using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using OneOf;
using ServiceNotFound = OneOf.Types.NotFound;
using ServiceSuccess = OneOf.Types.Success;

namespace LeoClassroom.Util;

/// <summary>
///     Translations of the service outcomes that recur across every resource into their HTTP results.
///     Each one still matches exhaustively; they exist so that the same not-found/forbidden/commit decision is not
///     written out once per endpoint.
/// </summary>
/// <remarks>
///     A <c>Results&lt;...&gt;</c> union does not convert to a wider one, so an endpoint that can also answer 400
///     needs its own variant here. Those are the <c>...OrInvalid</c> members - the extra
///     <see cref="ValidationProblem" /> case is never produced by these helpers, it only widens the declared union
///     to the one the endpoint returns from its own validation branch.
/// </remarks>
public static class Outcomes
{
    extension<TValue>(OneOf<TValue, ServiceNotFound> outcome)
    {
        /// <summary>
        ///     Answers 200 with the mapped value, or 404
        /// </summary>
        public Results<Ok<TDto>, NotFound> ToOk<TDto>(Func<TValue, TDto> toDto) =>
            outcome.Match<Results<Ok<TDto>, NotFound>>(
                value => TypedResults.Ok(toDto(value)),
                notFound => TypedResults.NotFound());

        /// <summary>
        ///     Answers 200 with the value itself, for the services that already return the response contract
        /// </summary>
        public Results<Ok<TValue>, NotFound> ToOk() => outcome.ToOk(value => value);
    }

    extension<TValue>(OneOf<TValue, ServiceNotFound, Forbidden> outcome)
    {
        /// <summary>
        ///     Answers 200 with the mapped value, 404, or 403
        /// </summary>
        public Results<Ok<TDto>, NotFound, ProblemHttpResult> ToOk<TDto>(Func<TValue, TDto> toDto) =>
            outcome.Match<Results<Ok<TDto>, NotFound, ProblemHttpResult>>(
                value => TypedResults.Ok(toDto(value)),
                notFound => TypedResults.NotFound(),
                forbidden => ApiResults.Forbidden());

        /// <summary>
        ///     Answers 200 with the value itself, for the services that already return the response contract
        /// </summary>
        public Results<Ok<TValue>, NotFound, ProblemHttpResult> ToOk() => outcome.ToOk(value => value);

        /// <summary>
        ///     Commits the endpoint's transaction and answers 200 with the mapped value; 404 and 403 are left
        ///     uncommitted on purpose, so that scoped unit-of-work disposal rolls the transaction back
        /// </summary>
        public ValueTask<Results<Ok<TDto>, ValidationProblem, NotFound, ProblemHttpResult>>
            CommitOkOrInvalidAsync<TDto>(ITransactionProvider transaction, Func<TValue, TDto> toDto) =>
            outcome.Match<ValueTask<Results<Ok<TDto>, ValidationProblem, NotFound, ProblemHttpResult>>>(
                async value =>
                {
                    await transaction.CommitAsync();

                    return TypedResults.Ok(toDto(value));
                },
                notFound => ValueTask.FromResult<Results<Ok<TDto>, ValidationProblem, NotFound, ProblemHttpResult>>(
                    TypedResults.NotFound()),
                forbidden => ValueTask.FromResult<Results<Ok<TDto>, ValidationProblem, NotFound, ProblemHttpResult>>(
                    ApiResults.Forbidden()));
    }

    extension(OneOf<ServiceSuccess, ServiceNotFound, Forbidden> outcome)
    {
        /// <summary>
        ///     Commits the endpoint's transaction and answers 204; 404 and 403 are left uncommitted on purpose
        /// </summary>
        public ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> CommitNoContentAsync(
            ITransactionProvider transaction) =>
            outcome.Match<ValueTask<Results<NoContent, NotFound, ProblemHttpResult>>>(
                async success =>
                {
                    await transaction.CommitAsync();

                    return TypedResults.NoContent();
                },
                notFound => ValueTask.FromResult<Results<NoContent, NotFound, ProblemHttpResult>>(
                    TypedResults.NotFound()),
                forbidden => ValueTask.FromResult<Results<NoContent, NotFound, ProblemHttpResult>>(
                    ApiResults.Forbidden()));

        /// <inheritdoc cref="CommitNoContentAsync" />
        public ValueTask<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>>
            CommitNoContentOrInvalidAsync(ITransactionProvider transaction) =>
            outcome.Match<ValueTask<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>>>(
                async success =>
                {
                    await transaction.CommitAsync();

                    return TypedResults.NoContent();
                },
                notFound => ValueTask.FromResult<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>>(
                    TypedResults.NotFound()),
                forbidden => ValueTask.FromResult<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>>(
                    ApiResults.Forbidden()));
    }
}
