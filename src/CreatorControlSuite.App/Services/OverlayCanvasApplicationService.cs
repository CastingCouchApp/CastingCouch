using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.App.Services;

public interface IOverlayCanvasApplicationService
{
    Task<OverlayCanvasSettings> CreateAsync(
        AppSettings settings,
        string name,
        CancellationToken cancellationToken = default);

    Task<OverlayCanvasSettings> RenameAsync(
        AppSettings settings,
        string canvasId,
        string name,
        CancellationToken cancellationToken = default);

    Task<OverlayCanvasSettings> DuplicateAsync(
        AppSettings settings,
        string sourceId,
        string name,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        AppSettings settings,
        string canvasId,
        CancellationToken cancellationToken = default);

    Task SelectAsync(
        AppSettings settings,
        string canvasId,
        CancellationToken cancellationToken = default);
}

public sealed class OverlayCanvasApplicationService(
    ISettingsStore settingsStore,
    IOverlayLayoutStore layoutStore,
    IOverlayWebServer webServer,
    IAppLogger logger) : IOverlayCanvasApplicationService
{
    public async Task<OverlayCanvasSettings> CreateAsync(
        AppSettings settings,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string normalizedName = NormalizeName(name);
        settings.Overlay.EnsureCanvasesMigrated();
        string previousSelected = settings.Overlay.SelectedCanvasId;
        string id = OverlaySettings.CreateCanvasId(
            normalizedName,
            settings.Overlay.Canvases.Select(canvas => canvas.Id));
        var canvas = new OverlayCanvasSettings
        {
            Id = id,
            Name = normalizedName
        };
        var layout = OverlayLayout.CreateDefault();
        layout.Name = normalizedName;

        await layoutStore.SaveAsync(id, layout, cancellationToken);
        settings.Overlay.Canvases.Add(canvas);
        settings.Overlay.SelectedCanvasId = id;

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Overlay.Canvases.Remove(canvas);
            settings.Overlay.SelectedCanvasId = previousSelected;
            await TryDeleteLayoutAsync(id, cancellationToken);
            throw;
        }

        await RefreshWebServerAsync(cancellationToken);
        return canvas;
    }

    public async Task<OverlayCanvasSettings> RenameAsync(
        AppSettings settings,
        string canvasId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string normalizedName = NormalizeName(name);
        OverlayCanvasSettings canvas = FindCanvas(settings, canvasId);
        string previousName = canvas.Name;
        OverlayLayout layout =
            await layoutStore.LoadAsync(canvas.Id, cancellationToken);
        string previousLayoutName = layout.Name;

        canvas.Name = normalizedName;
        layout.Name = normalizedName;
        await layoutStore.SaveAsync(canvas.Id, layout, cancellationToken);

        try
        {
            settings.Overlay.SelectedCanvasId = canvas.Id;
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            canvas.Name = previousName;
            layout.Name = previousLayoutName;
            await TrySaveLayoutAsync(
                canvas.Id,
                layout,
                cancellationToken);
            throw;
        }

        await RefreshWebServerAsync(cancellationToken);
        return canvas;
    }

    public async Task<OverlayCanvasSettings> DuplicateAsync(
        AppSettings settings,
        string sourceId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string normalizedName = NormalizeName(name);
        OverlayCanvasSettings source = FindCanvas(settings, sourceId);
        string previousSelected = settings.Overlay.SelectedCanvasId;
        string id = OverlaySettings.CreateCanvasId(
            normalizedName,
            settings.Overlay.Canvases.Select(canvas => canvas.Id));
        var duplicate = new OverlayCanvasSettings
        {
            Id = id,
            Name = normalizedName
        };

        await layoutStore.DuplicateAsync(
            source.Id,
            id,
            cancellationToken);
        OverlayLayout layout =
            await layoutStore.LoadAsync(id, cancellationToken);
        layout.Name = normalizedName;
        await layoutStore.SaveAsync(id, layout, cancellationToken);
        settings.Overlay.Canvases.Add(duplicate);
        settings.Overlay.SelectedCanvasId = id;

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Overlay.Canvases.Remove(duplicate);
            settings.Overlay.SelectedCanvasId = previousSelected;
            await TryDeleteLayoutAsync(id, cancellationToken);
            throw;
        }

        await RefreshWebServerAsync(cancellationToken);
        return duplicate;
    }

    public async Task DeleteAsync(
        AppSettings settings,
        string canvasId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Overlay.EnsureCanvasesMigrated();
        if (settings.Overlay.Canvases.Count <= 1)
        {
            throw new InvalidOperationException(
                "Das letzte Canvas kann nicht gelöscht werden.");
        }

        OverlayCanvasSettings canvas = FindCanvas(settings, canvasId);
        int index = settings.Overlay.Canvases.IndexOf(canvas);
        string previousSelected = settings.Overlay.SelectedCanvasId;
        settings.Overlay.Canvases.RemoveAt(index);
        settings.Overlay.EnsureCanvasesMigrated();

        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Overlay.Canvases.Insert(index, canvas);
            settings.Overlay.SelectedCanvasId = previousSelected;
            throw;
        }

        await TryDeleteLayoutAsync(canvas.Id, cancellationToken);
        await RefreshWebServerAsync(cancellationToken);
    }

    public async Task SelectAsync(
        AppSettings settings,
        string canvasId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OverlayCanvasSettings canvas = FindCanvas(settings, canvasId);
        string previousSelected = settings.Overlay.SelectedCanvasId;
        if (string.Equals(
                previousSelected,
                canvas.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.Overlay.SelectedCanvasId = canvas.Id;
        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            settings.Overlay.SelectedCanvasId = previousSelected;
            throw;
        }
    }

    private static OverlayCanvasSettings FindCanvas(
        AppSettings settings,
        string canvasId)
    {
        settings.Overlay.EnsureCanvasesMigrated();
        return settings.Overlay.Canvases.FirstOrDefault(canvas =>
                   string.Equals(
                       canvas.Id,
                       canvasId,
                       StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Overlay-Canvas '{canvasId}' wurde nicht gefunden.");
    }

    private static string NormalizeName(string name)
    {
        string normalized = name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException(
                "Der Canvas-Name darf nicht leer sein.",
                nameof(name));
        }

        return normalized;
    }

    private async Task RefreshWebServerAsync(
        CancellationToken cancellationToken)
    {
        if (!webServer.IsRunning)
        {
            return;
        }

        try
        {
            await webServer.RefreshMountedCanvasesAsync(
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Write(
                AppLogLevel.Warning,
                "Overlay",
                "Canvas-Liste im Webserver konnte nicht aktualisiert werden: " +
                exception.Message,
                exception);
        }
    }

    private async Task TryDeleteLayoutAsync(
        string canvasId,
        CancellationToken cancellationToken)
    {
        try
        {
            await layoutStore.DeleteAsync(canvasId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Write(
                AppLogLevel.Warning,
                "Overlay",
                $"Verwaistes Canvas-Layout '{canvasId}' konnte nicht entfernt werden.",
                exception);
        }
    }

    private async Task TrySaveLayoutAsync(
        string canvasId,
        OverlayLayout layout,
        CancellationToken cancellationToken)
    {
        try
        {
            await layoutStore.SaveAsync(
                canvasId,
                layout,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Write(
                AppLogLevel.Error,
                "Overlay",
                $"Canvas-Layout '{canvasId}' konnte nach einem Persistenzfehler nicht zurückgesetzt werden.",
                exception);
        }
    }
}
