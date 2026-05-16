using System.Windows;

namespace AutoRAW;

public partial class PromptDialog : Window
{
    private readonly bool _isPassword;

    public PromptDialog(string title, string message, string defaultValue = "", bool isPassword = false)
    {
        InitializeComponent();
        _isPassword = isPassword;
        Title = title;
        MessageText.Text = message;
        if (isPassword)
        {
            InputText.Visibility = Visibility.Collapsed;
            InputPassword.Visibility = Visibility.Visible;
            InputPassword.Focus();
        }
        else
        {
            InputText.Text = defaultValue;
            InputText.Focus();
            InputText.SelectAll();
        }
    }

    public string? Result { get; private set; }

    private void OkClick(object sender, RoutedEventArgs e)
    {
        Result = _isPassword ? InputPassword.Password : InputText.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
