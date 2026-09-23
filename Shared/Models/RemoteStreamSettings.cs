using Remotely.Shared.Enums;

namespace Remotely.Shared.Models;

public sealed record RemoteStreamSettings(
    int Version,
    RemoteStreamProfile Profile,
    int ImageQuality,
    int? MaxFps,
    RemoteAudioMode AudioMode)
{
    public static RemoteStreamSettings Original =>
        new(1, RemoteStreamProfile.Original, 80, null, RemoteAudioMode.Original);

    public static RemoteStreamSettings Balanced =>
        new(1, RemoteStreamProfile.Balanced, 65, 15, RemoteAudioMode.Off);

    public static RemoteStreamSettings Emergency =>
        new(1, RemoteStreamProfile.Emergency, 45, 8, RemoteAudioMode.Off);

    public static RemoteStreamSettings UltraLow =>
        new(1, RemoteStreamProfile.UltraLow, 30, 5, RemoteAudioMode.Off);

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

        if (Profile == RemoteStreamProfile.Original &&
            (ImageQuality != 80 || MaxFps is not null || AudioMode != RemoteAudioMode.Original))
        {
            error = "Original profile must preserve upstream behavior.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
