using System.ComponentModel;
using System.Windows;
using MianMianPet.ViewModels;

namespace MianMianPet.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Save_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveAsync();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsWorking) e.Cancel = true;
    }
}
