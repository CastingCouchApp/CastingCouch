using System.Windows;
using System.Windows.Input;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue ?? "";
        ValueBox.SelectAll();
        Loaded += (_, _) => ValueBox.Focus();
        ValueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
        };
        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            StatusText.Text = "Bitte einen Namen eingeben.";
            return;
        }

        DialogResult = true;
        Close();
    }
}
