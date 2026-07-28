using System.Collections.ObjectModel;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed record OverlayCanvasNameRequest(
    string Title,
    string Prompt,
    string InitialValue);

public sealed record OverlayWidgetUrlItem(
    string Label,
    string Path);

public sealed class OverlayCanvasPageViewModel : ViewModelBase
{
    private readonly IOverlayCanvasApplicationService _service;
    private AppSettings? _settings;
    private bool _loading;

    public OverlayCanvasPageViewModel(
        IOverlayCanvasApplicationService service)
    {
        _service = service;
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync());
        RenameCommand = new AsyncRelayCommand(
            _ => RenameAsync(),
            _ => SelectedCanvas is not null);
        DuplicateCommand = new AsyncRelayCommand(
            _ => DuplicateAsync(),
            _ => SelectedCanvas is not null);
        DeleteCommand = new AsyncRelayCommand(
            _ => DeleteAsync(),
            _ => SelectedCanvas is not null);
        CopyViewUrlCommand = new RelayCommand(CopyViewUrl);
        CopyWidgetUrlCommand = new RelayCommand(CopyWidgetUrl);
        OpenEditorCommand = new AsyncRelayCommand(
            _ => OpenEditorAsync(),
            _ => SelectedCanvas is not null);

        foreach (OverlayWidgetUrlItem item in CreateWidgetCatalog())
        {
            WidgetUrls.Add(item);
        }

        SelectedWidget = WidgetUrls.FirstOrDefault();
    }

    public ObservableCollection<OverlayCanvasSettings> Canvases { get; } = [];

    public OverlayCanvasSettings? SelectedCanvas
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            RefreshUrls();
            RaiseCommandStates();
            if (!_loading && value is not null)
            {
                _ = SelectSafelyAsync(value);
            }
        }
    }

    public ObservableCollection<OverlayWidgetUrlItem> WidgetUrls { get; } = [];

    public OverlayWidgetUrlItem? SelectedWidget
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string ViewUrl
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    public string EditorUrl
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit.";

    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand RenameCommand { get; }
    public AsyncRelayCommand DuplicateCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand CopyViewUrlCommand { get; }
    public RelayCommand CopyWidgetUrlCommand { get; }
    public AsyncRelayCommand OpenEditorCommand { get; }

    public Func<OverlayCanvasNameRequest, Task<string?>>?
        PromptNameRequestedAsync
    { get; set; }

    public Func<OverlayCanvasSettings, Task<bool>>?
        ConfirmDeleteRequestedAsync
    { get; set; }

    public Func<string, string, Task>? OpenEditorRequestedAsync { get; set; }

    public Action<string>? CopyTextRequested { get; set; }

    public Action<string>? ErrorRequested { get; set; }

    public void Load(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        RefreshCanvases(settings.Overlay.SelectedCanvasId);
    }

    public void UpdatePort(int port)
    {
        if (_settings is null || port is <= 0 or > 65535)
        {
            return;
        }

        _settings.Overlay.WebServerPort = port;
        RefreshUrls();
    }

    public void UpdateStatus(string status) =>
        Status = status;

    private async Task CreateAsync()
    {
        if (_settings is null || PromptNameRequestedAsync is null)
        {
            return;
        }

        string? name = await PromptNameRequestedAsync(
            new OverlayCanvasNameRequest(
                "Neues Canvas",
                "Name für das neue Overlay-Canvas:",
                ""));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSafelyAsync(
            async () =>
            {
                OverlayCanvasSettings canvas =
                    await _service.CreateAsync(_settings, name);
                RefreshCanvases(canvas.Id);
                Status = $"Canvas „{canvas.Name}“ angelegt.";
            },
            "Canvas konnte nicht angelegt werden");
    }

    private async Task RenameAsync()
    {
        if (_settings is null ||
            SelectedCanvas is null ||
            PromptNameRequestedAsync is null)
        {
            return;
        }

        OverlayCanvasSettings selected = SelectedCanvas;
        string? name = await PromptNameRequestedAsync(
            new OverlayCanvasNameRequest(
                "Canvas umbenennen",
                "Neuer Anzeigename (URL/Id bleibt gleich):",
                selected.Name));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSafelyAsync(
            async () =>
            {
                OverlayCanvasSettings canvas =
                    await _service.RenameAsync(
                        _settings,
                        selected.Id,
                        name);
                RefreshCanvases(canvas.Id);
                Status = $"Canvas umbenannt in „{canvas.Name}“.";
            },
            "Canvas konnte nicht umbenannt werden");
    }

    private async Task DuplicateAsync()
    {
        if (_settings is null ||
            SelectedCanvas is null ||
            PromptNameRequestedAsync is null)
        {
            return;
        }

        OverlayCanvasSettings selected = SelectedCanvas;
        string suggestedName = selected.Name.EndsWith(
            " (Kopie)",
            StringComparison.Ordinal)
            ? selected.Name
            : selected.Name + " (Kopie)";
        string? name = await PromptNameRequestedAsync(
            new OverlayCanvasNameRequest(
                "Canvas duplizieren",
                "Name für die Kopie:",
                suggestedName));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSafelyAsync(
            async () =>
            {
                OverlayCanvasSettings canvas =
                    await _service.DuplicateAsync(
                        _settings,
                        selected.Id,
                        name);
                RefreshCanvases(canvas.Id);
                Status = $"Canvas „{canvas.Name}“ dupliziert.";
            },
            "Canvas konnte nicht dupliziert werden");
    }

    private async Task DeleteAsync()
    {
        if (_settings is null || SelectedCanvas is null)
        {
            return;
        }

        OverlayCanvasSettings selected = SelectedCanvas;
        if (ConfirmDeleteRequestedAsync is null ||
            !await ConfirmDeleteRequestedAsync(selected))
        {
            return;
        }

        await RunSafelyAsync(
            async () =>
            {
                await _service.DeleteAsync(_settings, selected.Id);
                RefreshCanvases(_settings.Overlay.SelectedCanvasId);
                Status = $"Canvas „{selected.Name}“ gelöscht.";
            },
            "Canvas konnte nicht gelöscht werden");
    }

    private async Task SelectSafelyAsync(
        OverlayCanvasSettings canvas)
    {
        if (_settings is null)
        {
            return;
        }

        await RunSafelyAsync(
            () => _service.SelectAsync(_settings, canvas.Id),
            "Canvas-Auswahl konnte nicht gespeichert werden");
    }

    private void RefreshCanvases(string selectedId)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.Overlay.EnsureCanvasesMigrated();
        _loading = true;
        try
        {
            Canvases.Clear();
            foreach (OverlayCanvasSettings canvas in
                     _settings.Overlay.Canvases)
            {
                Canvases.Add(canvas);
            }

            SelectedCanvas = Canvases.FirstOrDefault(canvas =>
                                 string.Equals(
                                     canvas.Id,
                                     selectedId,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? Canvases.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshUrls()
    {
        if (_settings is null || SelectedCanvas is null)
        {
            ViewUrl = "";
            EditorUrl = "";
            return;
        }

        ViewUrl = _settings.Overlay.GetViewUrl(SelectedCanvas.Id);
        EditorUrl = _settings.Overlay.GetEditorUrl(SelectedCanvas.Id);
    }

    private void CopyViewUrl() =>
        Copy(ViewUrl, "Canvas-View-URL kopiert: ");

    private void CopyWidgetUrl()
    {
        if (_settings is null)
        {
            return;
        }

        string path = SelectedWidget?.Path ?? "music";
        string url = _settings.Overlay.GetWidgetUrl(path);
        Copy(url, "Widget-URL kopiert: ");
    }

    private void Copy(string value, string statusPrefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Status = "Keine URL verfügbar.";
            return;
        }

        try
        {
            CopyTextRequested?.Invoke(value);
            Status = statusPrefix + value;
        }
        catch (Exception exception)
        {
            Status = "URL konnte nicht kopiert werden: " +
                     exception.Message;
        }
    }

    private Task OpenEditorAsync()
    {
        if (SelectedCanvas is null ||
            OpenEditorRequestedAsync is null ||
            string.IsNullOrWhiteSpace(EditorUrl))
        {
            return Task.CompletedTask;
        }

        return OpenEditorRequestedAsync(
            EditorUrl,
            SelectedCanvas.Name);
    }

    private async Task RunSafelyAsync(
        Func<Task> action,
        string errorPrefix)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Status = errorPrefix + ": " + exception.Message;
            ErrorRequested?.Invoke(Status);
        }
    }

    private void RaiseCommandStates()
    {
        RenameCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        OpenEditorCommand.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<OverlayWidgetUrlItem>
        CreateWidgetCatalog() =>
    [
        new("Widget: Online + Zeit", "online"),
        new("Widget: Alert", "alert"),
        new("Widget: Music Player", "music"),
        new("Widget: Music Player (Legacy Spotify-URL)", "spotify"),
        new("Widget: Chat", "chat"),
        new("Widget: Ending Stats", "ending-stats"),
        new("Widget: Text", "text"),
        new("Widget: Image", "image"),
        new("Widget: Countdown", "countdown"),
        new("Widget: Socials", "socials"),
        new("Widget: Partner Roulette", "partner-roulette"),
        new("Widget: Goal Bar", "goal-bar"),
        new("Widget: Event Ticker", "event-ticker"),
        new("Widget: Viewer Count", "viewer-count"),
        new("Widget: Lower Third", "lower-third"),
        new("Widget: QR Code", "qr-code"),
        new("Widget: BRB Panel", "brb-panel"),
        new("Widget: Announcement Bar", "announcement-bar"),
        new("Widget: Bubatz Cantina", "bubatz-cantina"),
        new("Widget: fruppis Landadel", "fruppis-landadel"),
        new("Widget: Animated Background", "animated-background"),
        new("Shape: Frame", "shape/frame"),
        new("Shape: Card Frame", "shape/frame.card"),
        new("Shape: Vignette", "shape/shape.vignette"),
        new("Shape: Cutout", "shape/shape.cutout"),
        new("Shape: Starting Hintergrund", "shape/shape.scene-bg"),
        new("Shape: Divider", "shape/shape.divider"),
        new("Shape: Cam Ring", "shape/shape.cam-ring"),
        new("Shape: Sticker", "shape/shape.sticker")
    ];
}
