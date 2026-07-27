using System.Windows;
using CreatorControlSuite.Core.Legal;
namespace CreatorControlSuite.App.Views.Dialogs;

public partial class LegalConsentWindow : Window
{
    private readonly ILegalConsentService _service;
    public LegalConsentWindow(ILegalConsentService service) { InitializeComponent(); _service = service; Loaded += (_, _) => LoadDocs(); AcceptButton.Click += async (_, _) => await AcceptAsync(); CancelButton.Click += (_, _) => { DialogResult = false; Close(); }; }
    private void LoadDocs()
    {
        foreach (LegalDocumentInfo d in _service.GetDocuments())
        {
            string c = File.Exists(d.FilePath) ? File.ReadAllText(d.FilePath) : "Dokument fehlt: " + d.FilePath; if (d.Id == "eula")
            {
                EulaTextBox.Text = c;
            }
            else if (d.Id == "privacy")
            {
                PrivacyTextBox.Text = c;
            }
        }
    }
    private async Task AcceptAsync() { if (AcceptEulaBox.IsChecked != true || AcknowledgePrivacyBox.IsChecked != true) { MessageBox.Show("Bitte beide Bestätigungen aktivieren.", "Creator Control Suite", MessageBoxButton.OK, MessageBoxImage.Information); return; } await _service.SaveAcceptedAsync(); DialogResult = true; Close(); }
}
