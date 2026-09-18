using OneOf;

namespace LeoClassroom.Services.Util;

/// <summary>
///     The one exhaustive guard for the two-case <c>OneOf&lt;value, failure&gt;</c> outcomes that services and
///     integrations hand back.
/// </summary>
/// <remarks>
///     <para>
///         The positional accessors <c>IsT1</c>/<c>AsT1</c> are not allowed anywhere in this solution. They keep
///         compiling when a case is inserted into a union and then silently name a different case, which is how a
///         failure ends up being treated as a success. Every outcome is therefore read through <c>Match</c> or
///         <c>Switch</c>, which the compiler forces to cover every case.
///     </para>
///     <para>
///         This helper is that <c>Match</c>, written once: it turns "propagate the failure, otherwise carry on"
///         into a flat guard instead of nesting one match lambda inside the next. A caller that needs the success
///         value, or that handles a union of three or more cases, matches directly.
///     </para>
/// </remarks>
public static class Outcome
{
    extension<TValue, TError>(OneOf<TValue, TError> outcome) where TError : struct
    {
        /// <summary>
        ///     The failure case, or <c>null</c> when the outcome succeeded
        /// </summary>
        public TError? Failure => outcome.Match<TError?>(value => null, failure => failure);
    }
}
