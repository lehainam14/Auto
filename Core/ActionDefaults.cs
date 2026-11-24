using System.IO;
using System.Text.Json;
using Macro.Localization;

namespace Macro.Core;

/// <summary>
/// Manages global shared default values for actions (e.g. default click coordinates,
/// default find-image template). Persisted in %APPDATA%\macro\config.json so defaults
/// are restored across application sessions.
/// </summary>
public static class ActionDefaults
{
    private static readonly object _lock = new();

    public static event Action DefaultsChanged;

    public static bool HasDefaultCoordinates { get; private set; }
    public static int DefaultX { get; private set; }
    public static int DefaultY { get; private set; }
    public static int DefaultTabIndex { get; private set; } = -1;

    public static bool HasDefaultImageTemplate { get; private set; }
    public static string DefaultImagePath { get; private set; } = "";
    public static int DefaultImageTabIndex { get; private set; } = -1;

    public static string ConfigPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "macro");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    /// <summary>
    /// Loads shared action defaults from config.json.
    /// </summary>
    public static void LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                var path = ConfigPath();
                if (!File.Exists(path)) return;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                // Load default click coordinates
                if (root.TryGetProperty("default_click_coordinates", out var coordEl)
                    && coordEl.ValueKind == JsonValueKind.Object)
                {
                    int x = 0, y = 0, tabIdx = -1;
                    if (coordEl.TryGetProperty("x", out var xEl) && xEl.TryGetInt32(out var px)) x = px;
                    if (coordEl.TryGetProperty("y", out var yEl) && yEl.TryGetInt32(out var py)) y = py;
                    if (coordEl.TryGetProperty("tab_index", out var tabEl) && tabEl.TryGetInt32(out var pTab)) tabIdx = pTab;

                    DefaultX = x;
                    DefaultY = y;
                    DefaultTabIndex = tabIdx;
                    HasDefaultCoordinates = true;
                }
                else
                {
                    HasDefaultCoordinates = false;
                }

                // Default image template is strictly session-only.
                // Do not restore from previous sessions across application restarts.
                HasDefaultImageTemplate = false;
                DefaultImagePath = "";
                DefaultImageTabIndex = -1;
            }
            catch
            {
                // Corrupt or unreadable config -> keep current in-memory state
            }
        }
        DefaultsChanged?.Invoke();
    }

    /// <summary>
    /// Sets the shared default click coordinates and persists to config.json.
    /// </summary>
    public static void SetDefaultCoordinates(int x, int y, int tabIndex = -1)
    {
        lock (_lock)
        {
            DefaultX = x;
            DefaultY = y;
            DefaultTabIndex = tabIndex;
            HasDefaultCoordinates = true;
            SaveConfigInternal();
        }
        DefaultsChanged?.Invoke();
    }

    /// <summary>
    /// Clears the shared default click coordinates and updates config.json.
    /// </summary>
    public static void ClearDefaultCoordinates()
    {
        lock (_lock)
        {
            HasDefaultCoordinates = false;
            DefaultX = 0;
            DefaultY = 0;
            DefaultTabIndex = -1;
            SaveConfigInternal();
        }
        DefaultsChanged?.Invoke();
    }

    /// <summary>
    /// Sets the shared default image template for Find Image actions during the current session only.
    /// Does not persist across application restarts.
    /// </summary>
    public static void SetDefaultImageTemplate(string imagePath, int tabIndex = -1)
    {
        lock (_lock)
        {
            var resolved = Macro.Core.Services.ScreenshotService.ResolveImagePath(imagePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                ClearDefaultImageTemplate();
                return;
            }
            DefaultImagePath = resolved;
            DefaultImageTabIndex = tabIndex;
            HasDefaultImageTemplate = true;
            // Session-only: do NOT persist to config.json
        }
        DefaultsChanged?.Invoke();
    }

    /// <summary>
    /// Clears the shared default image template for the current session.
    /// </summary>
    public static void ClearDefaultImageTemplate()
    {
        lock (_lock)
        {
            HasDefaultImageTemplate = false;
            DefaultImagePath = "";
            DefaultImageTabIndex = -1;
            SaveConfigInternal();
        }
        DefaultsChanged?.Invoke();
    }

    private static void SaveConfigInternal()
    {
        try
        {
            var configPath = ConfigPath();
            Dictionary<string, object> root = new();

            if (File.Exists(configPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name != "default_click_coordinates" && prop.Name != "default_image_template")
                        {
                            root[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                        }
                    }
                }
                catch { }
            }

            if (HasDefaultCoordinates)
            {
                root["default_click_coordinates"] = new Dictionary<string, object>
                {
                    ["x"] = DefaultX,
                    ["y"] = DefaultY,
                    ["tab_index"] = DefaultTabIndex,
                };
            }
            else
            {
                root.Remove("default_click_coordinates");
            }

            // Image template is strictly session-only; ensure default_image_template is never saved to config.json
            root.Remove("default_image_template");

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(root, options));
        }
        catch { }
    }

    /// <summary>
    /// Automatically applies the shared defaults to a newly created ActionStep.
    /// Only sets default values if the shared default is configured.
    /// Does not overwrite step parameters if defaults are not set.
    /// </summary>
    public static void ApplyDefaultsToNewStep(ActionStep step)
    {
        if (step == null) return;

        if (step.Type is "click" or "double_click" or "right_click")
        {
            if (HasDefaultCoordinates)
            {
                step.Params["x"] = DefaultX;
                step.Params["y"] = DefaultY;
                if (DefaultTabIndex >= 0)
                {
                    step.Params["tab_index"] = DefaultTabIndex;
                }
            }
        }
        else if (step.Type is "find_image" or "click_image" or "wait_for_image" or "image_exists")
        {
            if (HasDefaultImageTemplate && !string.IsNullOrEmpty(DefaultImagePath) && File.Exists(DefaultImagePath))
            {
                step.Params["image"] = DefaultImagePath;
                if (DefaultImageTabIndex >= 0)
                {
                    step.Params["tab_index"] = DefaultImageTabIndex;
                }
            }
        }
    }
}
