internal static class AgentApiResults
{
    private const string ProblemTypeBase =
        "https://creator-control-suite.local/problems/";

    internal static IResult Unauthorized() =>
        Problem(
            StatusCodes.Status401Unauthorized,
            "Authentifizierung erforderlich.",
            "agent.authentication_required");

    internal static IResult Forbidden(string? permission = null) =>
        Problem(
            StatusCodes.Status403Forbidden,
            "Berechtigung fehlt.",
            "agent.permission_required",
            string.IsNullOrWhiteSpace(permission)
                ? null
                : $"Erforderliche Berechtigung: {permission}");

    internal static IResult BadRequest(string detail) =>
        Problem(
            StatusCodes.Status400BadRequest,
            "Ungültige Anfrage.",
            "agent.invalid_request",
            detail);

    internal static IResult NotFound(string detail) =>
        Problem(
            StatusCodes.Status404NotFound,
            "Ressource nicht gefunden.",
            "agent.not_found",
            detail);

    internal static IResult PayloadTooLarge(long maximumBytes) =>
        Problem(
            StatusCodes.Status413PayloadTooLarge,
            "Anfrage ist zu groß.",
            "agent.payload_too_large",
            $"Maximal zulässig sind {maximumBytes} Bytes.");

    internal static IResult TooManyRequests(string detail) =>
        Problem(
            StatusCodes.Status429TooManyRequests,
            "Zu viele Anfragen.",
            "agent.rate_limited",
            detail);

    internal static IResult InternalError(Exception? exception = null)
    {
        _ = exception;
        return Problem(
            StatusCodes.Status500InternalServerError,
            "Interner Agent-Fehler.",
            "agent.internal_error");
    }

    private static IResult Problem(
        int statusCode,
        string title,
        string code,
        string? detail = null) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            type: ProblemTypeBase + code,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
}
