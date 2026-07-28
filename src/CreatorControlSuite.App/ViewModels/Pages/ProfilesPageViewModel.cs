using System.Collections.ObjectModel;
using System.Windows;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Profiles;
using Microsoft.Win32;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class ProfilesPageViewModel : ViewModelBase, IPageViewModel
{
    private readonly IProfileService _profiles;

    public ProfilesPageViewModel(IProfileService profiles)
    {
        _profiles = profiles;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync());
        ApplyCommand = new AsyncRelayCommand(_ => ApplyAsync(), _ => SelectedProfile is not null);
        ExportCommand = new AsyncRelayCommand(_ => ExportAsync(), _ => SelectedProfile is not null);
        ImportCommand = new AsyncRelayCommand(_ => ImportAsync());
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => SelectedProfile is not null);
    }

    public string Key => "profiles";
    public string Title => "Profile";

    public ObservableCollection<ProfileSummary> Profiles { get; } = [];

    public ProfileSummary? SelectedProfile
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            ApplyCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            _ = LoadSelectedAsync();
        }
    }

    public string Name
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string Description
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string StatusMessage
    {
        get;
        set => SetProperty(ref field, value);
    } = "Bereit.";

    public bool StatusIsError
    {
        get;
        set => SetProperty(ref field, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    /// <summary>Host refreshes settings after a profile is applied.</summary>
    public Func<Task>? AfterProfileAppliedAsync { get; set; }

    public event EventHandler? ProfilesChanged;

    public Task OnNavigatedToAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProfileSummary> list = await _profiles.ListAsync(cancellationToken);
        Profiles.Clear();
        foreach (ProfileSummary item in list)
        {
            Profiles.Add(item);
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadSelectedAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        CreatorProfile profile = await _profiles.LoadAsync(SelectedProfile.Id);
        Name = profile.Name;
        Description = profile.Description;
        StatusMessage = $"Zuletzt geändert: {profile.UpdatedAt:dd.MM.yyyy HH:mm}";
        StatusIsError = false;
    }

    private async Task CreateAsync()
    {
        try
        {
            string name = Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Profil " + DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            }

            CreatorProfile profile = await _profiles.CreateFromCurrentSettingsAsync(name, Description.Trim());
            await RefreshAsync();
            SelectedProfile = Profiles.FirstOrDefault(item => item.Id == profile.Id);
            StatusMessage = "Profil gespeichert.";
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
        }
    }

    private async Task ApplyAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Profil „{SelectedProfile.Name}“ anwenden?\n\nDie aktuellen Einstellungen werden ersetzt.",
            "Profil anwenden",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _profiles.ApplyAsync(SelectedProfile.Id);
        if (AfterProfileAppliedAsync is not null)
        {
            await AfterProfileAppliedAsync();
        }

        StatusMessage = "Profil wurde angewendet.";
        StatusIsError = false;
    }

    private async Task ExportAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CastingCouch Profil (*.ccsprofile)|*.ccsprofile",
            FileName = SelectedProfile.Name + ".ccsprofile"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _profiles.ExportAsync(SelectedProfile.Id, dialog.FileName);
        StatusMessage = "Profil exportiert: " + dialog.FileName;
        StatusIsError = false;
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CastingCouch Profil (*.ccsprofile;*.json)|*.ccsprofile;*.json"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _profiles.ImportAsync(dialog.FileName);
        await RefreshAsync();
        StatusMessage = "Profil importiert.";
        StatusIsError = false;
    }

    private async Task DeleteAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Profil „{SelectedProfile.Name}“ löschen?",
            "Profil löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _profiles.DeleteAsync(SelectedProfile.Id);
        await RefreshAsync();
        StatusMessage = "Profil gelöscht.";
        StatusIsError = false;
    }
}
