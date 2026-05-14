using System.Windows;

namespace AutoRAW;

public partial class AlphaDisclaimerDialog : Window
{
    public AlphaDisclaimerDialog()
    {
        InitializeComponent();
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
