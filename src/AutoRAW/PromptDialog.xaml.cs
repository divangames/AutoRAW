using System.Windows;

namespace AutoRAW;

public partial class PromptDialog : Window
{
    public PromptDialog(string title, string message, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        InputText.Text = defaultValue;
        InputText.Focus();
        InputText.SelectAll();
    }

    public string? Result { get; private set; }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        Result = InputText.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
