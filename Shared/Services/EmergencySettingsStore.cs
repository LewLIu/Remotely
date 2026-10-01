using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Remotely.Shared.Models;

namespace Remotely.Shared.Services;

public sealed class EmergencySettingsStore : IEmergencySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _settingsPath;

    public EmergencySettingsStore(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("Settings path is required.", nameof(settingsPath));
        }

        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public static EmergencySettingsStore CreateDefault()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Remotely",
            "EmergencySettings.json");

        return new EmergencySettingsStore(path);
    }

    public RemoteStreamSettings Load(out string? warning)
    {
        warning = null;

        if (!File.Exists(_settingsPath))
        {
            return RemoteStreamSettings.Original;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<RemoteStreamSettings>(json, JsonOptions);

            if (settings is null)
            {
                warning = "Emergency settings file is empty.";
                return RemoteStreamSettings.Original;
            }

            if (!settings.TryValidate(out var validationError))
            {
                warning = validationError;
                return RemoteStreamSettings.Original;
            }

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warning = $"Unable to load emergency settings: {ex.Message}";
            return RemoteStreamSettings.Original;
        }
    }

    public async Task SaveAsync(
        RemoteStreamSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.TryValidate(out var validationError))
        {
            throw new ArgumentException(validationError, nameof(settings));
        }

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Emergency settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = _settingsPath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(
                tempPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
