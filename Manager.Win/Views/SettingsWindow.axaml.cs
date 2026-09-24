using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Remotely.Manager.Win.ViewModels;

namespace Remotely.Manager.Win.Views;

public partial class SettingsWindow : Window
{
    private TextBlock _errorText = null!;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _errorText = this.FindControl<TextBlock>("ErrorText")!;
    }

    private void RestoreDefaults_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.RestoreDefaults();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;

        try
        {
            _errorText.Text = string.Empty;
            await viewModel.SaveAsync();
            Close();
        }
        catch (Exception ex)
        {
            _errorText.Text = ex.Message;
        }
    }
}
