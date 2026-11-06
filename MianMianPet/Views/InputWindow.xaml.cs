using System.ComponentModel;
using System.Windows;
using MianMianPet.ViewModels;

namespace MianMianPet.Views;

public partial class InputWindow : Window
{
    private readonly PetViewModel _viewModel;
    private bool _allowClose;

    public InputWindow(PetViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void AllowClose() => _allowClose = true;

    public void ActivateInput()
    {
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        InputPanelControl.FocusInput();
        Dispatcher.InvokeAsync(InputPanelControl.FocusInput);
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _viewModel.HideInput();
        Hide();
    }
}
