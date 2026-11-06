using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MianMianPet.ViewModels;

namespace MianMianPet.Views;

public partial class InputPanel : UserControl
{
    private bool _isImeComposing;

    public InputPanel()
    {
        InitializeComponent();
        TextCompositionManager.AddPreviewTextInputStartHandler(MessageInput, OnImeCompositionStarted);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(MessageInput, OnImeCompositionUpdated);
        TextCompositionManager.AddPreviewTextInputHandler(MessageInput, OnImeCompositionCompleted);
        MessageInput.TextChanged += (_, _) => UpdatePlaceholderVisibility();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _isImeComposing = false;
                UpdatePlaceholderVisibility();
            }
        };
        UpdatePlaceholderVisibility();
    }

    public void FocusInput()
    {
        MessageInput.Focus();
        Keyboard.Focus(MessageInput);
        MessageInput.CaretIndex = MessageInput.Text.Length;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var actualKey = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

        if (actualKey == Key.Escape && !_isImeComposing && DataContext is PetViewModel escapeViewModel)
        {
            escapeViewModel.HideInputCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift && DataContext is PetViewModel viewModel)
        {
            if (_isImeComposing)
            {
                return;
            }

            if (viewModel.SendCommand.CanExecute(null))
            {
                viewModel.SendCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnImeCompositionStarted(object sender, TextCompositionEventArgs e)
    {
        _isImeComposing = true;
        PlaceholderText.Visibility = Visibility.Collapsed;
    }

    private void OnImeCompositionUpdated(object sender, TextCompositionEventArgs e)
    {
        _isImeComposing = true;
        PlaceholderText.Visibility = Visibility.Collapsed;
    }

    private void OnImeCompositionCompleted(object sender, TextCompositionEventArgs e)
    {
        _isImeComposing = false;
        Dispatcher.InvokeAsync(UpdatePlaceholderVisibility, DispatcherPriority.Background);
    }

    private void UpdatePlaceholderVisibility()
    {
        PlaceholderText.Visibility = !_isImeComposing && string.IsNullOrEmpty(MessageInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
