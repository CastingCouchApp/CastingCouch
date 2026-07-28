using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CreatorControlSuite.App.Views.Pages.Settings;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
        SaveSettingsButton.Click += async (_, _) =>
        {
            try
            {
                await (SaveRequestedAsync?.Invoke() ??
                    throw new InvalidOperationException(
                        "Speicheraktion ist nicht konfiguriert."));
            }
            catch (Exception exception)
            {
                ApplySaveResult(exception.Message, success: false);
            }
        };
    }

    public Func<Task>? SaveRequestedAsync { get; set; }

    public void SelectTab(int tabIndex)
    {
        if (tabIndex >= 0 &&
            tabIndex < SettingsTabControl.Items.Count)
        {
            SettingsTabControl.SelectedIndex = tabIndex;
        }
    }

    public void ApplySaveResult(
        string message,
        bool success)
    {
        SettingsStatusText.Text = message;
        SettingsStatusText.Foreground = success
            ? Brushes.LightGreen
            : Brushes.IndianRed;
    }
}
