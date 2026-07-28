using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class AlertLibraryPageViewModelTests
{
    [Fact]
    public void Load_SortsDefinitionsAndSelectsPreferredType()
    {
        var viewModel = new AlertLibraryPageViewModel(
            new FakeAlertService());
        var settings = new AppSettings();

        viewModel.Load(settings, "Sub");

        Assert.Equal(
            settings.Alerts.Definitions.Keys.OrderBy(
                type => type,
                StringComparer.OrdinalIgnoreCase),
            viewModel.Items.Select(item => item.Type));
        Assert.Equal("Sub", viewModel.SelectedItem?.Type);
    }

    [Fact]
    public async Task DuplicateCommand_SavesEditorBeforeDelegating()
    {
        var service = new FakeAlertService();
        var viewModel = new AlertLibraryPageViewModel(service);
        var settings = new AppSettings();
        viewModel.Load(settings, "Follow");
        bool editorSaved = false;
        viewModel.BeforeDuplicateRequestedAsync = () =>
        {
            editorSaved = true;
            return Task.CompletedTask;
        };

        viewModel.DuplicateCommand.Execute(null);
        await WaitUntilAsync(() => service.LastOperation is not null);

        Assert.True(editorSaved);
        Assert.Equal("duplicate:Follow", service.LastOperation);
        Assert.Equal("Follow Kopie", viewModel.SelectedItem?.Type);
    }

    [Fact]
    public async Task DeleteCommand_RequiresConfirmation()
    {
        var service = new FakeAlertService();
        var viewModel = new AlertLibraryPageViewModel(service);
        var settings = new AppSettings();
        viewModel.Load(settings, "Follow");
        viewModel.ConfirmDeleteRequestedAsync =
            _ => Task.FromResult(false);

        viewModel.DeleteCommand.Execute(null);
        await Task.Delay(25);
        Assert.Null(service.LastOperation);

        viewModel.ConfirmDeleteRequestedAsync =
            _ => Task.FromResult(true);
        viewModel.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => service.LastOperation is not null);

        Assert.Equal("delete:Follow", service.LastOperation);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 30 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeAlertService :
        IAlertDefinitionApplicationService
    {
        public string? LastOperation { get; private set; }

        public Task<AlertDefinitionSettings> CreateAsync(
            AppSettings settings,
            string baseType,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "create:" + baseType;
            var definition = new AlertDefinitionSettings
            {
                Type = baseType
            };
            settings.Alerts.Definitions[baseType] = definition;
            return Task.FromResult(definition);
        }

        public Task<AlertDefinitionSettings> DuplicateAsync(
            AppSettings settings,
            string sourceType,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "duplicate:" + sourceType;
            var definition = new AlertDefinitionSettings
            {
                Type = sourceType + " Kopie"
            };
            settings.Alerts.Definitions[definition.Type] = definition;
            return Task.FromResult(definition);
        }

        public Task<AlertDefinitionSettings> ToggleAsync(
            AppSettings settings,
            string type,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "toggle:" + type;
            AlertDefinitionSettings definition =
                settings.Alerts.Definitions[type];
            definition.Enabled = !definition.Enabled;
            return Task.FromResult(definition);
        }

        public Task DeleteAsync(
            AppSettings settings,
            string type,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "delete:" + type;
            settings.Alerts.Definitions.Remove(type);
            return Task.CompletedTask;
        }
    }
}
