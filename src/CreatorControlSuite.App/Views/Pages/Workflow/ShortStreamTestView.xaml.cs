using System.Windows.Controls;

namespace CreatorControlSuite.App.Views.Pages.Workflow;

public partial class ShortStreamTestView : UserControl
{
    public ShortStreamTestView()
    {
        InitializeComponent();
    }

    public void SetStatus(string status)
        => ShortStreamTestStatusText.Text = status;
}
