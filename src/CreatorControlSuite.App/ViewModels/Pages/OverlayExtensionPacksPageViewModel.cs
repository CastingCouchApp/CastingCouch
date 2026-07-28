using System.Collections.ObjectModel;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Modules.Overlay.Extensions;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed record OverlayExtensionPackItem(
    OverlayExtensionPackSummary Pack,
    string DisplayText);

public sealed class OverlayExtensionPacksPageViewModel : ViewModelBase
{
    private readonly IOverlayExtensionStore _store;

    public OverlayExtensionPacksPageViewModel(
        IOverlayExtensionStore store)
    {
        _store = store;
        ImportCommand = new AsyncRelayCommand(_ => ImportAsync());
        UninstallCommand = new AsyncRelayCommand(
            _ => UninstallAsync(),
            _ => SelectedPack is not null);
    }

    public ObservableCollection<OverlayExtensionPackItem> Packs { get; } = [];

    public OverlayExtensionPackItem? SelectedPack
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                UninstallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Keine Extension Packs installiert.";

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand UninstallCommand { get; }

    public Func<Task<Stream?>>? OpenPackRequestedAsync { get; set; }

    public Func<OverlayExtensionPackSummary, Task<bool>>?
        ConfirmUninstallRequestedAsync
    { get; set; }

    public Action<string, bool>? ErrorRequested { get; set; }

    public void Refresh()
    {
        Packs.Clear();
        foreach (OverlayExtensionPackSummary pack in
                 _store.ListCatalog())
        {
            Packs.Add(new OverlayExtensionPackItem(
                pack,
                $"{pack.Name} ({pack.Id}) · v{pack.Version} · " +
                $"{pack.Widgets.Count} Widget(s), " +
                $"{pack.Effects.Count} Effekt(e), " +
                $"{pack.Fonts.Count} Font(s)"));
        }

        SelectedPack = null;
        Status = Packs.Count == 0
            ? "Keine Extension Packs installiert."
            : $"{Packs.Count} Extension Pack(s) installiert.";
    }

    private async Task ImportAsync()
    {
        if (OpenPackRequestedAsync is null)
        {
            return;
        }

        try
        {
            await using Stream? stream = await OpenPackRequestedAsync();
            if (stream is null)
            {
                return;
            }

            OverlayExtensionPackSummary summary =
                await _store.InstallFromZipAsync(stream);
            Refresh();
            Status =
                $"Extension Pack „{summary.Name}“ ({summary.Id}) installiert.";
        }
        catch (OverlayExtensionValidationException exception)
        {
            SetError(
                "Import fehlgeschlagen: " + exception.Message,
                validationError: true);
        }
        catch (Exception exception)
        {
            SetError(
                "Import fehlgeschlagen: " + exception.Message,
                validationError: false);
        }
    }

    private async Task UninstallAsync()
    {
        if (SelectedPack is null)
        {
            Status = "Bitte zuerst ein Extension Pack auswählen.";
            return;
        }

        OverlayExtensionPackSummary pack = SelectedPack.Pack;
        if (ConfirmUninstallRequestedAsync is null ||
            !await ConfirmUninstallRequestedAsync(pack))
        {
            return;
        }

        try
        {
            await _store.UninstallAsync(pack.Id);
            Refresh();
            Status = $"Extension Pack „{pack.Name}“ deinstalliert.";
        }
        catch (Exception exception)
        {
            SetError(
                "Deinstallation fehlgeschlagen: " + exception.Message,
                validationError: false);
        }
    }

    private void SetError(
        string message,
        bool validationError)
    {
        Status = message;
        ErrorRequested?.Invoke(message, validationError);
    }
}
