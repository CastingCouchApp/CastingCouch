using System.Text.Json;

internal static class AgentApiExceptionHandling
{
    internal static async Task HandleAsync(
        HttpContext context,
        RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (JsonException)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            await AgentApiResults.BadRequest("Der JSON-Body ist ungültig.")
                .ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            ILogger logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("CreatorControlSuite.Agent.Api");
            logger.LogError(
                "Unbehandelter Agent-Fehler vom Typ {ExceptionType}, " +
                "correlationId={CorrelationId}.",
                ex.GetType().Name,
                context.TraceIdentifier);
            await AgentApiResults.InternalError(ex).ExecuteAsync(context);
        }
    }
}
