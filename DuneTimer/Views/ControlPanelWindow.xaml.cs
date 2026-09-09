using System.Windows;

namespace DuneTimer.Views;

public partial class ControlPanelWindow : Window
{
    public ControlPanelWindow()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
