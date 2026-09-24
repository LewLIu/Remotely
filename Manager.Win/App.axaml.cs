using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Remotely.Manager.Win.Services;
using Remotely.Manager.Win.ViewModels;
using Remotely.Manager.Win.Views;
using Remotely.Shared.Services;

namespace Remotely.Manager.Win;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private DispatcherTimer? _refreshTimer;
    private ManagerCoordinator? _coordinator;
    private IEmergencySettingsStore? _settingsStore;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusItem;
    private NativeMenuItem? _startItem;
    private NativeMenuItem? _stopItem;
    private readonly TrayIconStateProvider _iconProvider = new();
    private RemotelyServiceState? _renderedState;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _settingsStore = EmergencySettingsStore.CreateDefault();
            _coordinator = new ManagerCoordinator(new WindowsRemotelyServiceController());
            CreateTrayIcon();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            desktop.ShutdownRequested += Desktop_ShutdownRequested;
            _ = InitializeManagerAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        _statusItem = new NativeMenuItem("Service: Checking...") { IsEnabled = false };
        _startItem = new NativeMenuItem("Start Service");
        _stopItem = new NativeMenuItem("Stop Service");
        var settingsItem = new NativeMenuItem("Settings...");
        var exitItem = new NativeMenuItem("Exit");

        _startItem.Click += StartItem_Click;
        _stopItem.Click += StopItem_Click;
        settingsItem.Click += SettingsItem_Click;
        exitItem.Click += ExitItem_Click;

        var menu = new NativeMenu
        {
            _statusItem,
            new NativeMenuItemSeparator(),
            _startItem,
            _stopItem,
            settingsItem,
            new NativeMenuItemSeparator(),
            exitItem
        };

        _trayIcon = new TrayIcon
        {
            Icon = _iconProvider.Create(RemotelyServiceState.Error),
            ToolTipText = "Remotely - Checking service",
            Menu = menu,
            IsVisible = true
        };

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private async Task InitializeManagerAsync()
    {
        if (_coordinator is null) return;
        await _coordinator.InitializeAsync();
        UpdateTrayState();
        if (_coordinator.CurrentState == RemotelyServiceState.Error)
            ShowError(_coordinator.LastError ?? "Unable to start Remotely_Service.");
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_coordinator is null) return;
        await _coordinator.RefreshAsync();
        UpdateTrayState();
    }

    private async void StartItem_Click(object? sender, EventArgs e)
    {
        if (_coordinator is null) return;
        if (!await _coordinator.StartServiceAsync())
            ShowError(_coordinator.LastError ?? "Unable to start Remotely_Service.");
        UpdateTrayState();
    }

    private async void StopItem_Click(object? sender, EventArgs e)
    {
        if (_coordinator is null) return;
        if (!await _coordinator.StopServiceAsync())
            ShowError(_coordinator.LastError ?? "Unable to stop Remotely_Service.");
        UpdateTrayState();
    }

    private void SettingsItem_Click(object? sender, EventArgs e)
    {
        if (_settingsStore is null) return;
        var window = new SettingsWindow(new SettingsViewModel(_settingsStore));
        window.Show();
    }

    private async void ExitItem_Click(object? sender, EventArgs e)
    {
        if (_coordinator is null || _desktopLifetime is null) return;

        if (!await _coordinator.TryExitAsync(TimeSpan.FromSeconds(10)))
        {
            UpdateTrayState();
            ShowError(
                _coordinator.LastError ??
                "Remotely_Service could not be confirmed stopped. Remote access may still be enabled.");
            return;
        }

        _refreshTimer?.Stop();
        if (_trayIcon is not null) _trayIcon.IsVisible = false;
        _desktopLifetime.Shutdown();
    }

    private void Desktop_ShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_coordinator is null) return;
        try
        {
            _coordinator.TryExitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        catch
        {
            // OS shutdown/logoff is time constrained. This is best-effort only.
        }
    }

    private void UpdateTrayState()
    {
        if (_coordinator is null || _trayIcon is null || _statusItem is null || _startItem is null || _stopItem is null)
            return;

        var state = _coordinator.CurrentState;
        _statusItem.Header = $"Service: {state}";
        _startItem.IsEnabled = state is RemotelyServiceState.Stopped or RemotelyServiceState.Error;
        _stopItem.IsEnabled = state is RemotelyServiceState.Running or RemotelyServiceState.StartPending;
        _trayIcon.ToolTipText = $"Remotely - {state}";

        if (_renderedState != state)
        {
            _trayIcon.Icon = _iconProvider.Create(state);
            _renderedState = state;
        }
    }

    private static void ShowError(string message)
    {
        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var window = new Window
        {
            Title = "Remotely Manager",
            Width = 440,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    closeButton
                }
            }
        };
        closeButton.Click += (_, _) => window.Close();
        window.Show();
    }
}
