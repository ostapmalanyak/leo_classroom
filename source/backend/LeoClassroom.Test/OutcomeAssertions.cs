using OneOf;

namespace LeoClassroom.Test;

/// <summary>
///     Asserts which case a <c>OneOf</c> outcome carries, by naming its type rather than its position
/// </summary>
/// <remarks>
///     The positional <c>IsT1</c> assertion keeps compiling when a case is inserted into a union, and from then on
///     asserts something the test never meant - a test that passes for the wrong reason. Naming the case type
///     fails to compile instead, which is the same reason production code reads outcomes through
///     <c>Match</c>/<c>Switch</c> only.
/// </remarks>
internal static class OutcomeAssertions
{
    extension(IOneOf outcome)
    {
        /// <summary>
        ///     Asserts that the outcome carries the <typeparamref name="TCase" /> case
        /// </summary>
        /// <returns>
        ///     That case, so a test that also wants to look inside it does not need a second accessor
        /// </returns>
        public TCase ShouldBe<TCase>() => outcome.Value.Should().BeAssignableTo<TCase>().Subject;
    }
}
