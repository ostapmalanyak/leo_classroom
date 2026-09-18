using Microsoft.AspNetCore.Http.HttpResults;

namespace LeoClassroom.Util;

/// <summary>
///     Shared RFC 9457 problem responses for outcomes that carry no payload, so that every endpoint reports the
///     same error shape as failed validation and unhandled exceptions do
/// </summary>
public static class ApiResults
{
    /// <summary>
    ///     The caller is authenticated but not allowed to act on this resource
    /// </summary>
    /// <remarks>
    ///     Deliberately carries no detail: the reason a teacher may not touch a foreign course is not the
    ///     caller's business, and spelling it out would confirm the resource exists.
    /// </remarks>
    public static ProblemHttpResult Forbidden() =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

    /// <summary>
    ///     An upstream service (Forgejo, Moodle, LDAP, SMTP) could not be reached or answered unusably
    /// </summary>
    /// <param name="detail">What failed, in terms that are safe to show a teacher</param>
    public static ProblemHttpResult BadGateway(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status502BadGateway, title: "Upstream service failed");
}
