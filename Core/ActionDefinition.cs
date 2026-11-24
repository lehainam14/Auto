namespace Macro.Core;

/// <summary>Widget types the Properties panel knows how to build.</summary>
public enum ParamWidget
{
    Text,           // single line
    MultilineText,  // paragraph
    Int,
    Double,
    Bool,
    Enum,           // dropdown, Options = [(label, value), ...]
    Slider,         // double between Min..Max
    Key,            // key name + "Capture..." button
    ImageFile,      // path + preview + browse
    File,           // path + browse
    Position,       // X/Y fields + "Select on screen" picker
    Region,         // screen region picker (x, y, w, h + enable)
    StepSelector,   // dropdown selecting another step in the workflow
}

/// <summary>One editable parameter of an action (declarative form schema).</summary>
public class ParamSpec
{
    public string Key;
    public string Label;
    public ParamWidget Widget = ParamWidget.Text;
    public object Default;
    public List<(string Label, object Value)> Options;   // for Enum
    public double? Min;
    public double? Max;
    public string Unit = "";
    public string Tooltip = "";

    public ParamSpec(string key, string label, ParamWidget widget = ParamWidget.Text)
    {
        Key = key;
        Label = label;
        Widget = widget;
    }
}

/// <summary>Function that runs one action:  execute(context, step).</summary>
public delegate void ActionExecute(ExecutionContext ctx, ActionStep step);

/// <summary>Optional pre-run validation: return an error message or null.</summary>
public delegate string ActionValidate(ActionStep step);

/// <summary>
/// Everything the app knows about one action type. The Properties panel
/// builds its form from ParamSchema, the library sidebar shows Name/Icon,
/// and the engine calls Execute.
/// </summary>
public class ActionDefinition
{
    public string TypeId;
    public string Name;
    public string Category;
    public string Icon;                    // logical name -> UI/Icons.cs
    public string Description;
    public List<ParamSpec> ParamSchema = new();
    public Dictionary<string, object> DefaultParams = new();
    public Func<Dictionary<string, object>, string> Summary;   // card text
    public ActionExecute Execute;                              // null for containers
    public ActionValidate Validate;                            // optional pre-check
    public bool IsContainer;                                   // Loop, If/Else ...
    public bool HiddenFromLibrary;                             // hide obsolete/redundant actions from UI library
}

/// <summary>Category metadata (display order + accent color).</summary>
public record ActionCategory(string Id, string Label, string Color);

public static class Categories
{
    public static readonly List<ActionCategory> All = new()
    {
        new("mouse", "Mouse", "#4F8CFF"),
        new("keyboard", "Keyboard", "#3FB96F"),
        new("screen", "Image / Screen", "#B07CF0"),
        new("timing", "Timing", "#E0A53F"),
        new("logic", "Logic", "#F0764F"),
        new("application", "Application", "#2FB8C6"),
    };

    public static string LabelOf(string id) =>
        All.FirstOrDefault(c => c.Id == id)?.Label ?? id;

    public static string ColorOf(string id) =>
        All.FirstOrDefault(c => c.Id == id)?.Color ?? "#C7CAD1";
}
