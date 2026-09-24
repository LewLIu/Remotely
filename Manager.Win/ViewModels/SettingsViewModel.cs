using System.ComponentModel;
using System.Runtime.CompilerServices;
using Remotely.Shared.Enums;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Manager.Win.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IEmergencySettingsStore _store;
    private bool _applyingSnapshot;
    private bool _enableAudio;
    private int _imageQuality;
    private int? _maxFps;
    private RemoteStreamProfile _profile;

    public SettingsViewModel(IEmergencySettingsStore store)
    {
        _store = store;
        Profiles = Enum.GetValues<RemoteStreamProfile>();
        var settings = _store.Load(out var warning);
        Warning = warning;
        ApplySnapshot(settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RemoteStreamProfile> Profiles { get; }
    public string? Warning { get; }

    public RemoteStreamProfile Profile
    {
        get => _profile;
        set
        {
            if (_applyingSnapshot)
            {
                _profile = value;
                return;
            }
            SelectPreset(value);
        }
    }

    public int ImageQuality
    {
        get => _imageQuality;
        set
        {
            if (value is < 20 or > 90)
                throw new ArgumentOutOfRangeException(nameof(value), "Image quality must be between 20 and 90.");
            if (_imageQuality == value) return;
            _imageQuality = value;
            OnPropertyChanged();
            if (!_applyingSnapshot) MarkCustom();
        }
    }

    public int? MaxFps
    {
        get => _maxFps;
        set
        {
            if (value is not null && (value < 2 || value > 30))
                throw new ArgumentOutOfRangeException(nameof(value), "Max FPS must be between 2 and 30.");
            if (_maxFps == value) return;
            _maxFps = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MaxFpsText));
            if (!_applyingSnapshot) MarkCustom();
        }
    }

    public bool EnableAudio
    {
        get => _enableAudio;
        set
        {
            if (_enableAudio == value) return;
            _enableAudio = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioText));
            if (!_applyingSnapshot) MarkCustom();
        }
    }

    public bool UsesOriginalBehavior => Profile == RemoteStreamProfile.Original;
    public string MaxFpsText => UsesOriginalBehavior ? "Original behavior" : MaxFps?.ToString() ?? "—";
    public string AudioText => UsesOriginalBehavior ? "Original behavior" : EnableAudio ? "On" : "Off";

    public void SelectPreset(RemoteStreamProfile profile)
    {
        if (profile == RemoteStreamProfile.Custom)
        {
            ApplySnapshot(new RemoteStreamSettings(
                1,
                RemoteStreamProfile.Custom,
                ImageQuality == 0 ? 80 : ImageQuality,
                MaxFps ?? 30,
                EnableAudio ? RemoteAudioMode.Original : RemoteAudioMode.Off));
            return;
        }

        ApplySnapshot(profile switch
        {
            RemoteStreamProfile.Original => RemoteStreamSettings.Original,
            RemoteStreamProfile.Balanced => RemoteStreamSettings.Balanced,
            RemoteStreamProfile.Emergency => RemoteStreamSettings.Emergency,
            RemoteStreamProfile.UltraLow => RemoteStreamSettings.UltraLow,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        });
    }

    public void RestoreDefaults() => ApplySnapshot(RemoteStreamSettings.Original);

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var settings = BuildSnapshot();
        if (!settings.TryValidate(out var error))
            throw new InvalidOperationException(error);
        await _store.SaveAsync(settings, cancellationToken);
    }

    private RemoteStreamSettings BuildSnapshot()
        => Profile switch
        {
            RemoteStreamProfile.Original => RemoteStreamSettings.Original,
            RemoteStreamProfile.Balanced => RemoteStreamSettings.Balanced,
            RemoteStreamProfile.Emergency => RemoteStreamSettings.Emergency,
            RemoteStreamProfile.UltraLow => RemoteStreamSettings.UltraLow,
            RemoteStreamProfile.Custom when MaxFps is not null => new RemoteStreamSettings(
                1,
                RemoteStreamProfile.Custom,
                ImageQuality,
                MaxFps,
                EnableAudio ? RemoteAudioMode.Original : RemoteAudioMode.Off),
            RemoteStreamProfile.Custom => throw new InvalidOperationException("Custom profile requires a Max FPS value."),
            _ => throw new ArgumentOutOfRangeException()
        };

    private void ApplySnapshot(RemoteStreamSettings settings)
    {
        _applyingSnapshot = true;
        try
        {
            _profile = settings.Profile;
            _imageQuality = settings.ImageQuality;
            _maxFps = settings.MaxFps;
            _enableAudio = settings.AudioMode != RemoteAudioMode.Off;
        }
        finally
        {
            _applyingSnapshot = false;
        }

        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(ImageQuality));
        OnPropertyChanged(nameof(MaxFps));
        OnPropertyChanged(nameof(EnableAudio));
        OnPropertyChanged(nameof(UsesOriginalBehavior));
        OnPropertyChanged(nameof(MaxFpsText));
        OnPropertyChanged(nameof(AudioText));
    }

    private void MarkCustom()
    {
        if (_profile == RemoteStreamProfile.Custom) return;
        _profile = RemoteStreamProfile.Custom;
        if (_maxFps is null) _maxFps = 30;
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(MaxFps));
        OnPropertyChanged(nameof(UsesOriginalBehavior));
        OnPropertyChanged(nameof(MaxFpsText));
        OnPropertyChanged(nameof(AudioText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
