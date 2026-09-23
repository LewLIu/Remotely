using System.IO;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Desktop.Win.Services;

public sealed class EmergencySettingsProviderWin : IRemoteStreamSettingsProvider, IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(150);

    private readonly object _sync = new();
    private readonly IEmergencySettingsStore _store;
    private readonly TimeSpan _debounceDelay;
    private readonly FileSystemWatcher _watcher;
    private Timer? _debounceTimer;
    private RemoteStreamSettings _current;
    private bool _disposed;

    public EmergencySettingsProviderWin()
        : this(GetDefaultSettingsPath(), DefaultDebounceDelay)
    {
    }

    public EmergencySettingsProviderWin(string settingsPath, TimeSpan debounceDelay)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("Settings path is required.", nameof(settingsPath));
        }
        if (debounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        }

        var fullPath = Path.GetFullPath(settingsPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Emergency settings path has no parent directory.");
        var fileName = Path.GetFileName(fullPath);

        Directory.CreateDirectory(directory);

        _store = new EmergencySettingsStore(fullPath);
        _debounceDelay = debounceDelay;
        _current = _store.Load(out _);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime |
                           NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnSettingsFileChanged;
        _watcher.Created += OnSettingsFileChanged;
        _watcher.Deleted += OnSettingsFileChanged;
        _watcher.Renamed += OnSettingsFileRenamed;
    }

    public RemoteStreamSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<RemoteStreamSettings>? SettingsChanged;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }

    private static string GetDefaultSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Remotely",
            "EmergencySettings.json");
    }

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs args)
    {
        ScheduleReload();
    }

    private void OnSettingsFileRenamed(object sender, RenamedEventArgs args)
    {
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ => Reload(),
                null,
                _debounceDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void Reload()
    {
        var next = _store.Load(out var warning);
        if (warning is not null)
        {
            return;
        }

        EventHandler<RemoteStreamSettings>? handler;
        lock (_sync)
        {
            if (_disposed || next == _current)
            {
                return;
            }

            _current = next;
            handler = SettingsChanged;
        }

        handler?.Invoke(this, next);
    }
}
