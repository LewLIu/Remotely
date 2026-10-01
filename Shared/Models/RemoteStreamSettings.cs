using Remotely.Shared.Enums;

namespace Remotely.Shared.Models;

public sealed record RemoteStreamSettings(
    int Version,
    RemoteStreamProfile Profile,
    int ImageQuality,
    int? MaxFps,
    RemoteAudioMode AudioMode,
    int? MaxStreamWidth = null,
    int? MaxStreamHeight = null,
    bool PreferLatestFrame = false)
{
    public static RemoteStreamSettings Original =>
        new(1, RemoteStreamProfile.Original, 80, null, RemoteAudioMode.Original);

    public static RemoteStreamSettings Balanced =>
        new(1, RemoteStreamProfile.Balanced, 55, 20, RemoteAudioMode.Off, 1600, 900);

    public static RemoteStreamSettings Emergency =>
        new(1, RemoteStreamProfile.Emergency, 35, 20, RemoteAudioMode.Off, 1280, 720);

    public static RemoteStreamSettings UltraLow =>
        new(1, RemoteStreamProfile.UltraLow, 20, 20, RemoteAudioMode.Off, 960, 540, true);

    public bool TryValidate(out string error)
    {
        if (Version != 1)
        {
            error = "Unsupported settings version.";
            return false;
        }

        if (ImageQuality is < 20 or > 90)
        {
            error = "Image quality must be between 20 and 90.";
            return false;
        }

        if (MaxFps is not null && (MaxFps < 2 || MaxFps > 30))
        {
            error = "Max FPS must be between 2 and 30.";
            return false;
        }

        if ((MaxStreamWidth is null) != (MaxStreamHeight is null))
        {
            error = "Stream width and height must both be set or both be native.";
            return false;
        }

        if (MaxStreamWidth is not null && (MaxStreamWidth < 640 || MaxStreamWidth > 7680))
        {
            error = "Max stream width must be between 640 and 7680.";
            return false;
        }

        if (MaxStreamHeight is not null && (MaxStreamHeight < 360 || MaxStreamHeight > 4320))
        {
            error = "Max stream height must be between 360 and 4320.";
            return false;
        }

        if (Profile == RemoteStreamProfile.Original &&
            (ImageQuality != 80 ||
             MaxFps is not null ||
             AudioMode != RemoteAudioMode.Original ||
             MaxStreamWidth is not null ||
             MaxStreamHeight is not null ||
             PreferLatestFrame))
        {
            error = "Original profile must preserve upstream behavior.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
