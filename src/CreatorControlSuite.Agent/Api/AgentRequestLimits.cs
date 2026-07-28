using Microsoft.AspNetCore.Http.Features;

internal static class AgentRequestLimits
{
    internal const long PairingBytes = 4L * 1024;
    internal const long JsonApiBytes = 1L * 1024 * 1024;
    internal const long UpdateStageBytes = 140L * 1024 * 1024;

    internal static long Resolve(string path) =>
        Resolve(new PathString(path));

    internal static long Resolve(PathString path)
    {
        if (path.Equals(
                new PathString("/api/v1/pair"),
                StringComparison.OrdinalIgnoreCase))
        {
            return PairingBytes;
        }

        if (path.Equals(
                new PathString("/api/v1/update/stage"),
                StringComparison.OrdinalIgnoreCase))
        {
            return UpdateStageBytes;
        }

        return JsonApiBytes;
    }

    internal static async Task EnforceAsync(
        HttpContext context,
        RequestDelegate next)
    {
        long maximumBytes = Resolve(context.Request.Path);
        IHttpMaxRequestBodySizeFeature? feature =
            context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maximumBytes;
        }

        if (context.Request.ContentLength is long contentLength &&
            contentLength > maximumBytes)
        {
            await AgentApiResults.PayloadTooLarge(maximumBytes)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
