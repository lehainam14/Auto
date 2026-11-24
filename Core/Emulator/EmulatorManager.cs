using System.IO;
using System.Text.Json;
using Macro.Localization;

namespace Macro.Core.Emulator;

/// <summary>
/// Manages the active emulator configuration, persistence in config.json,
/// and registered validators.
/// </summary>
public static class EmulatorManager
{
    private static readonly Dictionary<string, IEmulatorValidator> _validators = new();
    private static string _currentEmulatorId = "ldplayer";
    private static string _currentPath = "";
    private static EmulatorValidationResult _lastResult = EmulatorValidationResult.Fail(
        Loc.T("emu.err.not_configured", "Chưa cấu hình đường dẫn trình giả lập."));

    public static event Action ConfigurationChanged;

    public static string CurrentEmulatorId => _currentEmulatorId;
    public static string CurrentPath => _currentPath;
    public static EmulatorValidationResult LastResult => _lastResult;

    public static bool IsConfiguredAndValid => _lastResult is { IsValid: true };

    private static LdInstanceInfo _selectedInstance;
    public static LdInstanceInfo SelectedInstance
    {
        get => _selectedInstance;
        set => _selectedInstance = value;
    }

    public static List<LdInstanceInfo> GetLiveInstances()
    {
        return LdPlayerService.LiveInstances.Values.ToList();
    }

    static EmulatorManager()
    {
        RegisterValidator(new LdPlayerValidator());
    }

    public static void RegisterValidator(IEmulatorValidator validator)
    {
        _validators[validator.Id] = validator;
    }

    public static IReadOnlyCollection<IEmulatorValidator> GetValidators() => _validators.Values;

    public static IEmulatorValidator GetValidator(string id) =>
        _validators.TryGetValue(id, out var v) ? v : _validators.Values.FirstOrDefault();

    private static string ConfigPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "macro");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    /// <summary>
    /// Loads emulator configuration from config.json and validates it.
    /// </summary>
    public static void LoadConfig()
    {
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path))
            {
                _lastResult = EmulatorValidationResult.Fail(
                    Loc.T("emu.err.not_configured", "Chưa cấu hình đường dẫn trình giả lập."));
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("emulator", out var emuEl)
                && emuEl.ValueKind == JsonValueKind.Object)
            {
                var id = emuEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "ldplayer";
                var emuPath = emuEl.TryGetProperty("directory", out var dirEl) && !string.IsNullOrWhiteSpace(dirEl.GetString())
                    ? dirEl.GetString()
                    : (emuEl.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : "");

                _currentEmulatorId = string.IsNullOrEmpty(id) ? "ldplayer" : id;
                _currentPath = emuPath ?? "";

                if (!string.IsNullOrWhiteSpace(_currentPath))
                {
                    var validator = GetValidator(_currentEmulatorId);
                    if (validator != null)
                    {
                        _lastResult = validator.Validate(_currentPath);
                        if (_lastResult.IsValid)
                        {
                            _currentPath = _lastResult.DirectoryPath;
                        }
                    }
                    else
                    {
                        _lastResult = EmulatorValidationResult.Fail($"Loại giả lập không hỗ trợ: {_currentEmulatorId}");
                    }
                }
                else
                {
                    _lastResult = EmulatorValidationResult.Fail(
                        Loc.T("emu.err.not_configured", "Chưa cấu hình đường dẫn trình giả lập."));
                }
            }
            else
            {
                _lastResult = EmulatorValidationResult.Fail(
                    Loc.T("emu.err.not_configured", "Chưa cấu hình đường dẫn trình giả lập."));
            }
        }
        catch (Exception ex)
        {
            _lastResult = EmulatorValidationResult.Fail($"Lỗi đọc cấu hình: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates and saves the emulator configuration to config.json.
    /// Returns the validation result.
    /// </summary>
    public static EmulatorValidationResult SaveConfig(string emulatorId, string rawPath)
    {
        var validator = GetValidator(emulatorId);
        if (validator == null)
        {
            _lastResult = EmulatorValidationResult.Fail($"Loại giả lập không hỗ trợ: {emulatorId}");
            return _lastResult;
        }

        var result = validator.Validate(rawPath);
        _lastResult = result;
        _currentEmulatorId = emulatorId;
        _currentPath = result.IsValid ? result.DirectoryPath : (rawPath ?? "");

        if (result.IsValid)
        {
            try
            {
                var configFilePath = ConfigPath();
                Dictionary<string, object> root = new();

                if (File.Exists(configFilePath))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(configFilePath));
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Name != "emulator")
                            {
                                root[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                            }
                        }
                    }
                    catch { }
                }

                if (!root.ContainsKey("language"))
                {
                    root["language"] = Loc.Language;
                }

                root["emulator"] = new Dictionary<string, string>
                {
                    ["type"] = emulatorId,
                    ["path"] = result.DirectoryPath,
                    ["directory"] = result.DirectoryPath,
                    ["executable"] = result.ExecutablePath,
                    ["version"] = result.VersionInfo
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configFilePath, JsonSerializer.Serialize(root, options));
            }
            catch (Exception ex)
            {
                _lastResult = EmulatorValidationResult.Fail($"Không thể lưu cấu hình: {ex.Message}");
                return _lastResult;
            }
        }

        ConfigurationChanged?.Invoke();
        return result;
    }

    /// <summary>
    /// Validates path without saving.
    /// </summary>
    public static EmulatorValidationResult Validate(string emulatorId, string rawPath)
    {
        var validator = GetValidator(emulatorId);
        if (validator == null)
            return EmulatorValidationResult.Fail($"Loại giả lập không hỗ trợ: {emulatorId}");
        return validator.Validate(rawPath);
    }

    /// <summary>
    /// Resolves and returns the full path to ldconsole.exe based on currently configured directory.
    /// </summary>
    public static string GetLdConsolePath()
    {
        if (string.IsNullOrWhiteSpace(_currentPath)) return "";
        var tools = LdPlayerService.ResolveTools(_currentPath);
        return tools.LdConsolePath;
    }

    /// <summary>
    /// Loads sequential launch options from config.json.
    /// </summary>
    public static LdLaunchOptions LoadLaunchOptions()
    {
        var options = new LdLaunchOptions();
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path)) return options;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("ldplayer_launch", out var el) && el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("interval_seconds", out var iVal) && iVal.TryGetInt32(out var interval))
                    options.IntervalSeconds = Math.Max(0, interval);
                if (el.TryGetProperty("max_wait_seconds", out var wVal) && wVal.TryGetInt32(out var wait))
                    options.MaxWaitSeconds = Math.Max(5, wait);
                if (el.TryGetProperty("max_retries", out var rVal) && rVal.TryGetInt32(out var retries))
                    options.MaxRetries = Math.Max(0, retries);
            }
        }
        catch { }
        return options;
    }

    /// <summary>
    /// Saves sequential launch options to config.json.
    /// </summary>
    public static void SaveLaunchOptions(LdLaunchOptions options)
    {
        if (options == null) return;
        try
        {
            var configFilePath = ConfigPath();
            Dictionary<string, object> root = new();
            if (File.Exists(configFilePath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configFilePath));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name != "ldplayer_launch")
                        {
                            root[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                        }
                    }
                }
                catch { }
            }

            if (!root.ContainsKey("language"))
            {
                root["language"] = Loc.Language;
            }

            root["ldplayer_launch"] = new Dictionary<string, int>
            {
                ["interval_seconds"] = options.IntervalSeconds,
                ["max_wait_seconds"] = options.MaxWaitSeconds,
                ["max_retries"] = options.MaxRetries,
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configFilePath, JsonSerializer.Serialize(root, jsonOptions));
        }
        catch { }
    }
}

