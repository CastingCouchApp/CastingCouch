using System.Windows; using CreatorControlSuite.Core.Legal;
namespace CreatorControlSuite.App;
public partial class LegalConsentWindow : Window
{
    readonly ILegalConsentService _service;
    public LegalConsentWindow(ILegalConsentService service){InitializeComponent();_service=service;Loaded+=(_,_)=>LoadDocs();AcceptButton.Click+=async(_,_)=>await AcceptAsync();CancelButton.Click+=(_,_)=>{DialogResult=false;Close();};}
    void LoadDocs(){foreach(var d in _service.GetDocuments()){var c=File.Exists(d.FilePath)?File.ReadAllText(d.FilePath):"Dokument fehlt: "+d.FilePath;if(d.Id=="eula")EulaTextBox.Text=c;else if(d.Id=="privacy")PrivacyTextBox.Text=c;}}
    async Task AcceptAsync(){if(AcceptEulaBox.IsChecked!=true||AcknowledgePrivacyBox.IsChecked!=true){MessageBox.Show("Bitte beide Bestätigungen aktivieren.","Creator Control Suite",MessageBoxButton.OK,MessageBoxImage.Information);return;}await _service.SaveAcceptedAsync();DialogResult=true;Close();}
}
