using System.IO;
using System.Text.Json;

namespace Macro.Core;

/// <summary>
/// A text (.txt) file attached specifically to a macro.
/// </summary>
public class AttachedTxtFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
}

/// <summary>
/// One step inside a workflow. Container steps (e.g. Loop) hold children.
/// This class is pure data - no UI and no execution logic.
/// </summary>
public class ActionStep
{
    public string Type;
    public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string Name = "";                                   // custom label
    public bool Enabled = true;
    public Dictionary<string, object> Params = new();
    public List<ActionStep> Children = new();
    /// <summary>Second branch for the If/Else container (runs when the
    /// condition is false). Empty for all other steps.</summary>
    public List<ActionStep> ElseChildren = new();

    public ActionStep DeepCloneWithNewIds()
    {
        var clone = new ActionStep
        {
            Type = Type,
            Name = Name,
            Enabled = Enabled,
            Params = new Dictionary<string, object>(Params),
        };
        foreach (var child in Children)
            clone.Children.Add(child.DeepCloneWithNewIds());
        foreach (var child in ElseChildren)
            clone.ElseChildren.Add(child.DeepCloneWithNewIds());
        return clone;
    }

    /// <summary>Yield this step and every descendant (depth-first).</summary>
    public IEnumerable<ActionStep> Walk()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.Walk())
                yield return descendant;
        foreach (var child in ElseChildren)
            foreach (var descendant in child.Walk())
                yield return descendant;
    }
}

/// <summary>A macro: an ordered tree of steps plus workflow-level settings.</summary>
public class Workflow
{
    public string Name = "New Macro";
    public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
    public List<ActionStep> Actions = new();
    public List<AttachedTxtFile> AttachedFiles = new();
    public Dictionary<string, object> Settings = new()
    {
        ["repeat_workflow"] = 1,     // run the whole workflow N times
        ["step_delay_ms"] = 0,       // extra pause after every step
        ["failsafe"] = true,         // slam mouse to top-left corner = stop
    };

    public IEnumerable<ActionStep> Walk()
    {
        foreach (var step in Actions)
            foreach (var descendant in step.Walk())
                yield return descendant;
    }

    public int CountSteps() => Walk().Count();

    public ActionStep Find(string stepId) => Walk().FirstOrDefault(s => s.Id == stepId);

    /// <summary>The list that directly contains the step with this id.</summary>
    public List<ActionStep> ParentList(string stepId)
    {
        if (Actions.Any(s => s.Id == stepId)) return Actions;
        foreach (var step in Walk())
        {
            if (step.Children.Any(c => c.Id == stepId)) return step.Children;
            if (step.ElseChildren.Any(c => c.Id == stepId)) return step.ElseChildren;
        }
        return null;
    }

    /// <summary>True if candidateId is a descendant of (or equal to) ancestorId.</summary>
    public bool IsDescendant(string candidateId, string ancestorId)
    {
        var ancestor = Find(ancestorId);
        return ancestor != null
            && ancestor.Walk().Any(s => s.Id == candidateId);
    }

    /// <summary>
    /// Determines whether the user should be prompted to save changes when closing the application.
    /// Returns true only if there is at least one step in the workflow AND there are unsaved changes
    /// (either marked dirty or not yet saved to a file).
    /// Returns false if there are no steps or if all steps are already saved with no pending changes.
    /// </summary>
    public static bool ShouldPromptSaveOnClose(int stepCount, bool isDirty, bool hasFilePath)
    {
        if (stepCount <= 0) return false;
        bool hasUnsavedChanges = isDirty || !hasFilePath;
        return hasUnsavedChanges;
    }

    public bool ShouldPromptSaveOnClose(bool isDirty, string filePath)
        => ShouldPromptSaveOnClose(CountSteps(), isDirty, !string.IsNullOrEmpty(filePath));

    public Workflow Clone()
    {
        var wf = new Workflow
        {
            Name = Name,
            Id = Guid.NewGuid().ToString("N").Substring(0, 8),
            Settings = new Dictionary<string, object>(Settings),
            AttachedFiles = AttachedFiles.Select(f => new AttachedTxtFile
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = f.Name,
                FilePath = f.FilePath,
            }).ToList(),
        };
        foreach (var step in Actions)
            wf.Actions.Add(step.DeepCloneWithNewIds());
        return wf;
    }

    public AttachedTxtFile FindAttachedFile(string fileIdOrName)
    {
        if (string.IsNullOrWhiteSpace(fileIdOrName)) return null;
        return AttachedFiles.FirstOrDefault(f => f.Id == fileIdOrName)
            ?? AttachedFiles.FirstOrDefault(f => string.Equals(f.Name, fileIdOrName, StringComparison.OrdinalIgnoreCase))
            ?? AttachedFiles.FirstOrDefault(f => string.Equals(Path.GetFileName(f.FilePath), fileIdOrName, StringComparison.OrdinalIgnoreCase));
    }

    public string ResolveAttachedFilePath(AttachedTxtFile file, string macroFilePath = null)
    {
        if (file == null) return null;
        return ResolveTxtPath(file.FilePath, macroFilePath, file.Name, Id);
    }

    public static string ResolveTxtPath(string path, string macroFilePath = null, string fallbackFileName = null, string workflowId = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        // 1. Direct path check
        if (File.Exists(path)) return Path.GetFullPath(path);

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(fallbackFileName))
            fileName = fallbackFileName;

        // 2. Relative to macro file location
        if (!string.IsNullOrEmpty(macroFilePath))
        {
            var dir = Path.GetDirectoryName(macroFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var candidate1 = Path.Combine(dir, path);
                if (File.Exists(candidate1)) return Path.GetFullPath(candidate1);

                if (!string.IsNullOrEmpty(fileName))
                {
                    var candidate2 = Path.Combine(dir, fileName);
                    if (File.Exists(candidate2)) return Path.GetFullPath(candidate2);

                    var candidate3 = Path.Combine(dir, "files", fileName);
                    if (File.Exists(candidate3)) return Path.GetFullPath(candidate3);
                }
            }
        }

        // 3. In app data / user macro files folder
        if (!string.IsNullOrEmpty(fileName))
        {
            if (!string.IsNullOrEmpty(workflowId))
            {
                var wfFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "macro", "files", workflowId, fileName);
                if (File.Exists(wfFolder)) return Path.GetFullPath(wfFolder);
            }

            var commonFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "macro", "files", fileName);
            if (File.Exists(commonFolder)) return Path.GetFullPath(commonFolder);
        }

        return path;
    }

    public List<string> GetDiscoveredVariables()
    {
        var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "var1", "var2", "var3"
        };
        foreach (var step in Walk())
        {
            var v = step.GetStr("variable_name")?.Trim();
            if (!string.IsNullOrEmpty(v)) vars.Add(v);

            var text = step.GetStr("text");
            if (!string.IsNullOrEmpty(text) && text.Contains('{') && text.Contains('}'))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\{([a-zA-Z0-9_]+)\}");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    if (m.Groups.Count > 1) vars.Add(m.Groups[1].Value);
                }
            }
        }
        return vars.ToList();
    }

    public void BindToTab(int tabIndex)
    {
        foreach (var step in Walk())
        {
            step.Params["tab_index"] = tabIndex;
        }
    }

    /// <summary>
    /// Returns the ordered list of steps with their hierarchical numbers (#1, #2, #2.1, etc.).
    /// Used by StepSelector and jump actions to maintain consistent numbering.
    /// </summary>
    public List<StepDisplayInfo> GetIndexedSteps(string excludeId = null)
    {
        var list = new List<StepDisplayInfo>();
        void Traverse(List<ActionStep> steps, string prefix)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var num = $"{prefix}{i + 1}";
                var def = ActionRegistry.Get(s.Type);
                var actionName = def != null ? Localization.Loc.ActionName(def.TypeId) : s.Type;
                list.Add(new StepDisplayInfo
                {
                    Id = s.Id,
                    Number = num,
                    ActionName = actionName,
                    CustomName = s.Name,
                });
                if (s.Children.Count > 0)
                    Traverse(s.Children, num + ".");
                if (s.ElseChildren.Count > 0)
                    Traverse(s.ElseChildren, num + "E.");
            }
        }
        Traverse(Actions, "");
        if (!string.IsNullOrEmpty(excludeId))
            list.RemoveAll(x => x.Id == excludeId);
        return list;
    }

    /// <summary>
    /// Look up the display info (number and name) for a specific step ID.
    /// </summary>
    public StepDisplayInfo GetStepDisplayInfo(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return GetIndexedSteps().FirstOrDefault(x => x.Id == id);
    }
}

/// <summary>
/// Display metadata for a step when referenced by another step or shown in a list.
/// Combines the 1-based order number (#1, #2, #2.1, etc.) and action name.
/// </summary>
public class StepDisplayInfo
{
    public string Id { get; set; } = "";
    public string Number { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string CustomName { get; set; } = "";
    public string DisplayText => $"#{Number} - {(string.IsNullOrEmpty(CustomName) ? ActionName : CustomName)}";

    public override string ToString() => DisplayText;
}


/// <summary>
/// Helpers that read parameter values no matter whether they come from the
/// UI (plain int/double/string/bool) or from a loaded JSON file (JsonElement).
/// </summary>
public static class ParamUtil
{
    public static int ToInt(object value, int fallback = 0)
    {
        switch (value)
        {
            case null: return fallback;
            case int i: return i;
            case long l: return (int)l;
            case double d: return (int)Math.Round(d);
            case bool b: return b ? 1 : 0;
            case string s:
                return int.TryParse(s, out var parsed) ? parsed : fallback;
            case JsonElement element:
                if (element.ValueKind == JsonValueKind.Number)
                    return element.TryGetInt32(out var i32) ? i32
                         : (int)Math.Round(element.GetDouble());
                if (element.ValueKind == JsonValueKind.String
                    && int.TryParse(element.GetString(), out var fromString))
                    return fromString;
                return fallback;
            default: return fallback;
        }
    }

    public static double ToDouble(object value, double fallback = 0)
    {
        switch (value)
        {
            case null: return fallback;
            case int i: return i;
            case long l: return l;
            case double d: return d;
            case string s:
                return double.TryParse(s, out var parsed) ? parsed : fallback;
            case JsonElement element:
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetDouble();
                return fallback;
            default: return fallback;
        }
    }

    public static bool ToBool(object value, bool fallback = false)
    {
        switch (value)
        {
            case null: return fallback;
            case bool b: return b;
            case int i: return i != 0;
            case string s: return bool.TryParse(s, out var parsed) && parsed;
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => element.GetDouble() != 0,
                    _ => fallback,
                };
            default: return fallback;
        }
    }

    public static string ToStringValue(object value, string fallback = "")
    {
        switch (value)
        {
            case null: return fallback;
            case string s: return s;
            case JsonElement element:
                return element.ValueKind == JsonValueKind.String
                     ? element.GetString() ?? fallback
                     : element.GetRawText();
            default: return value.ToString();
        }
    }
}

/// <summary>Convenient accessors on a step's parameters.</summary>
public static class StepParamExtensions
{
    public static int GetInt(this ActionStep step, string key, int fallback = 0)
        => ParamUtil.ToInt(step.Params.TryGetValue(key, out var value) ? value : null, fallback);

    public static double GetDouble(this ActionStep step, string key, double fallback = 0)
        => ParamUtil.ToDouble(step.Params.TryGetValue(key, out var value) ? value : null, fallback);

    public static bool GetBool(this ActionStep step, string key, bool fallback = false)
        => ParamUtil.ToBool(step.Params.TryGetValue(key, out var value) ? value : null, fallback);

    public static string GetStr(this ActionStep step, string key, string fallback = "")
        => ParamUtil.ToStringValue(step.Params.TryGetValue(key, out var value) ? value : null, fallback);
}
