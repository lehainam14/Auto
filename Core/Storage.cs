using System.IO;
using System.Text;
using System.Text.Json;

namespace Macro.Core;

/// <summary>
/// Storage Layer: save / load workflows as JSON (*.macro or *.json).
/// Only the configuration is stored - never any user-written code.
/// </summary>
public static class WorkflowStorage
{
    public const string FileFilter = "Macro Workflow (*.macro)|*.macro|JSON (*.json)|*.json";
    public const int FormatVersion = 1;

    public static string DefaultFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "macro");
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ------------------------------------------------------------------- save
    public static void Save(Workflow workflow, string path)
    {
        var root = new Dictionary<string, object>
        {
            ["format"] = "macro",
            ["version"] = FormatVersion,
            ["id"] = workflow.Id,
            ["name"] = workflow.Name,
            ["settings"] = workflow.Settings,
            ["attached_files"] = workflow.AttachedFiles.Select(f => new Dictionary<string, object>
            {
                ["id"] = f.Id,
                ["name"] = f.Name,
                ["path"] = f.FilePath,
            }).ToList(),
            ["actions"] = workflow.Actions.Select(StepToDict).ToList(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.GetFullPath(path)) ?? ".");
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(root, options),
                          new UTF8Encoding(false));
    }

    private static Dictionary<string, object> StepToDict(ActionStep step)
    {
        var dict = new Dictionary<string, object>
        {
            ["id"] = step.Id,
            ["type"] = step.Type,
            ["name"] = step.Name,
            ["enabled"] = step.Enabled,
            ["params"] = step.Params,
        };
        if (step.Children.Count > 0)
            dict["children"] = step.Children.Select(StepToDict).ToList();
        if (step.ElseChildren.Count > 0)
            dict["children_else"] = step.ElseChildren.Select(StepToDict).ToList();
        return dict;
    }

    // ------------------------------------------------------------------- load
    /// <summary>Returns the workflow plus warnings for unknown action types
    /// (they are skipped instead of failing the whole file).</summary>
    public static (Workflow workflow, List<string> warnings) Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("actions", out _))
            throw new InvalidOperationException(
                "This file does not look like a macro workflow.");

        var workflow = new Workflow
        {
            Name = ReadString(root, "name", "New Macro"),
            Id = ReadString(root, "id", Guid.NewGuid().ToString("N").Substring(0, 8)),
        };

        if (root.TryGetProperty("settings", out var settingsElement)
            && settingsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in settingsElement.EnumerateObject())
                workflow.Settings[pair.Name] = JsonSerializer
                    .Deserialize<object>(pair.Value.GetRawText());
        }

        if (root.TryGetProperty("attached_files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filesEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var file = new AttachedTxtFile
                    {
                        Id = ReadString(item, "id", Guid.NewGuid().ToString("N").Substring(0, 8)),
                        Name = ReadString(item, "name", ""),
                        FilePath = ReadString(item, "path", ""),
                    };
                    var resolved = Workflow.ResolveTxtPath(file.FilePath, path, file.Name, workflow.Id);
                    if (File.Exists(resolved))
                    {
                        file.FilePath = resolved;
                    }
                    workflow.AttachedFiles.Add(file);
                }
            }
        }

        var warnings = new List<string>();
        if (root.TryGetProperty("actions", out var actionsElement)
            && actionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in actionsElement.EnumerateArray())
            {
                var step = StepFromDict(element, warnings, path);
                if (step.Type != null)          // null = unknown type, skipped
                    workflow.Actions.Add(step);
            }
        }
        return (workflow, warnings);
    }

    private static ActionStep StepFromDict(JsonElement element,
                                           List<string> warnings,
                                           string macroFilePath = null)
    {
        var step = new ActionStep
        {
            Type = ReadString(element, "type", ""),
            Id = ReadString(element, "id",
                            Guid.NewGuid().ToString("N").Substring(0, 8)),
            Name = ReadString(element, "name", ""),
            Enabled = ReadBool(element, "enabled", true),
        };

        if (ActionRegistry.Get(step.Type) == null)
        {
            warnings.Add($"Skipped unknown action type '{step.Type}'"
                         + (step.Name.Length > 0 ? $" (step '{step.Name}')" : ""));
            step.Type = null;   // dropped by the caller
            return step;
        }

        if (element.TryGetProperty("params", out var paramsElement)
            && paramsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in paramsElement.EnumerateObject())
                step.Params[pair.Name] = JsonSerializer
                    .Deserialize<object>(pair.Value.GetRawText());
        }

        // Backward compatibility migration:
        // Automatically migrate deprecated duplicate 'click_image' into unified 'find_image' with on_found='click'
        if (step.Type == "click_image")
        {
            step.Type = "find_image";
            step.Params["on_found"] = "click";
            if (!step.Params.ContainsKey("found_target_id"))
                step.Params["found_target_id"] = "";
            if (!step.Params.ContainsKey("not_found_target_id"))
                step.Params["not_found_target_id"] = "";
            if (string.IsNullOrEmpty(step.Name) || step.Name is "Click Image" or "Nhấp vào hình ảnh" or "Ấn vào hình ảnh")
            {
                step.Name = Localization.Loc.T("action.find_image", "Tìm hình ảnh");
            }
        }

        // Backward compatibility migration for read_txt_line name:
        if (step.Type == "read_txt_line")
        {
            if (string.IsNullOrEmpty(step.Name) || step.Name is "Đọc dòng tệp TXT" or "Đọc tệp TXT" or "Đọc dòng" or "Read TXT Line")
            {
                step.Name = Localization.Loc.T("action.read_txt_line", "Lấy văn bản");
            }
        }

        // Tự động phân giải đường dẫn ảnh mẫu nếu là đường dẫn tương đối hoặc tệp nằm trong templates
        if (step.Params.TryGetValue("image", out var imgObj) && imgObj != null)
        {
            var rawStr = ParamUtil.ToStringValue(imgObj);
            if (!string.IsNullOrWhiteSpace(rawStr))
            {
                var resolved = Services.ScreenshotService.ResolveImagePath(rawStr, macroFilePath);
                if (File.Exists(resolved))
                {
                    step.Params["image"] = resolved;
                }
            }
        }

        if (element.TryGetProperty("children", out var childrenElement)
            && childrenElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childrenElement.EnumerateArray())
            {
                var childStep = StepFromDict(child, warnings, macroFilePath);
                if (childStep.Type != null)
                    step.Children.Add(childStep);
            }
        }

        if (element.TryGetProperty("children_else", out var elseElement)
            && elseElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in elseElement.EnumerateArray())
            {
                var childStep = StepFromDict(child, warnings, macroFilePath);
                if (childStep.Type != null)
                    step.ElseChildren.Add(childStep);
            }
        }
        return step;
    }

    private static string ReadString(JsonElement element, string name,
                                     string fallback)
    {
        if (element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return fallback;
    }

    private static bool ReadBool(JsonElement element, string name,
                                 bool fallback)
    {
        if (element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return fallback;
    }
}
