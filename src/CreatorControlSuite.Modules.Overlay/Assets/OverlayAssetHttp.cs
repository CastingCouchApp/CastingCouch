using System.Net;
using CreatorControlSuite.Modules.Overlay.Assets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CreatorControlSuite.Modules.Overlay;

/// <summary>HTTP-Routen für die Overlay-Asset-Library (<c>/assets</c>).</summary>
public static class OverlayAssetHttp
{
    public static void MapRoutes(
        WebApplication app,
        IOverlayAssetStore assetStore,
        Func<HttpContext, bool>? allowMutations = null)
    {
        Func<HttpContext, bool> canMutate = allowMutations ?? IsLoopback;

        app.MapGet("/assets", () =>
        {
            var assets = assetStore.List().Select(a => new
            {
                id = a.Id,
                name = a.OriginalName,
                url = a.PublicUrl,
                contentType = a.ContentType,
                size = a.SizeBytes,
                createdAt = a.CreatedAt
            }).ToArray();
            return Results.Json(new { assets });
        });

        app.MapGet("/assets/{id}", (string id, HttpContext context) =>
        {
            if (!assetStore.TryGet(id, out OverlayAssetInfo asset))
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return Results.File(asset.LocalPath, asset.ContentType);
        });

        app.MapPost("/assets", async (HttpContext context) =>
        {
            if (!canMutate(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart/form-data mit Bilddatei erwartet" });
            }

            IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            IFormFile? file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "keine Bilddatei übermittelt" });
            }

            try
            {
                await using Stream stream = file.OpenReadStream();
                OverlayAssetInfo asset = await assetStore.ImportAsync(
                    stream,
                    file.FileName,
                    context.RequestAborted);
                return Results.Json(new
                {
                    id = asset.Id,
                    name = asset.OriginalName,
                    url = asset.PublicUrl,
                    contentType = asset.ContentType,
                    size = asset.SizeBytes,
                    createdAt = asset.CreatedAt
                });
            }
            catch (OverlayAssetValidationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/assets/{id}", async (string id, HttpContext context) =>
        {
            if (!canMutate(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                await assetStore.DeleteAsync(id, context.RequestAborted);
                return Results.Json(new { ok = true });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid asset id" });
            }
        });
    }

    public static bool IsLoopback(HttpContext context)
    {
        IPAddress? remote = context.Connection.RemoteIpAddress;
        return remote is null || IPAddress.IsLoopback(remote);
    }
}
