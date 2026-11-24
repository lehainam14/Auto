namespace Macro.Core;

/// <summary>
/// The Action System registry. Every action type registers an
/// ActionDefinition here. The library sidebar, the add-menu and the
/// properties panel all discover actions through this class, so adding a
/// new action requires no UI changes.
/// </summary>
public static class ActionRegistry
{
    private static readonly Dictionary<string, ActionDefinition> _defs = new();

    public static void Register(ActionDefinition definition)
    {
        if (_defs.ContainsKey(definition.TypeId))
            throw new InvalidOperationException(
                $"Duplicate action type: {definition.TypeId}");
        _defs[definition.TypeId] = definition;
    }

    public static ActionDefinition Get(string typeId)
        => _defs.TryGetValue(typeId, out var def) ? def : null;

    public static List<ActionDefinition> Definitions(string category = null, bool includeHidden = false)
        => _defs.Values.Where(d => (category == null || d.Category == category) && (includeHidden || !d.HiddenFromLibrary))
                       .ToList();

    public static List<string> UsedCategories()
        => Categories.All.Select(c => c.Id)
                         .Where(id => _defs.Values.Any(d => d.Category == id && !d.HiddenFromLibrary))
                         .ToList();

    /// <summary>Create a fresh step with default parameters, applying shared action defaults if configured.</summary>
    public static ActionStep CreateStep(string typeId)
    {
        var def = Get(typeId);
        if (def == null) return null;
        var step = new ActionStep
        {
            Type = typeId,
            Params = new Dictionary<string, object>(def.DefaultParams),
        };
        ActionDefaults.ApplyDefaultsToNewStep(step);
        return step;
    }
}
