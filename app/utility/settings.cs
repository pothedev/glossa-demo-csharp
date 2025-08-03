using System;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;

public static class Settings
{
    private static readonly string settingsPath = "settings.json";
    private static readonly ConcurrentDictionary<string, object> _settings = new();
    private static readonly object _fileLock = new();

    static Settings()
    {
        LoadSettings();
    }

    private static void LoadSettings()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var loaded = JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(json);
                    
                    _settings.Clear();
                    foreach (var kvp in loaded)
                    {
                        _settings[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to load settings: {ex.Message}");
                _settings.Clear(); // Start fresh if corrupted
            }
        }
    }

    public static T GetValue<T>(string key)
    {
        if (_settings.TryGetValue(key, out object value))
        {
            try
            {
                if (value is JsonElement element)
                {
                    return element.ValueKind switch
                    {
                        JsonValueKind.True => (T)(object)true,
                        JsonValueKind.False => (T)(object)false,
                        JsonValueKind.String => (T)(object)element.GetString(),
                        JsonValueKind.Number when typeof(T) == typeof(int) => (T)(object)element.GetInt32(),
                        JsonValueKind.Number when typeof(T) == typeof(double) => (T)(object)element.GetDouble(),
                        _ => JsonSerializer.Deserialize<T>(element.GetRawText())
                    };
                }
                return (T)value;
            }
            catch
            {
                Console.WriteLine($"⚠️ Type mismatch for key '{key}'. Returning default.");
                return default;
            }
        }
        return default;
    }

    public static void SetValue<T>(string key, T value)
    {
        _settings[key] = value;
        SaveSettings();
    }

    private static void SaveSettings()
    {
        lock (_fileLock)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(_settings, options));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to save settings: {ex.Message}");
            }
        }
    }
}