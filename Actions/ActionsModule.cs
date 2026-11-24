using Macro.Core;

namespace Macro.Actions;

/// <summary>
/// Registers every built-in action. Called once at application startup and
/// by the self-test. To add a new action: create a static Register() method
/// in a new class here and call it from this file. Nothing else changes.
/// </summary>
public static class ActionsModule
{
    private static bool _registered;

    public static void RegisterAll()
    {
        if (_registered) return;
        _registered = true;
        MouseActions.Register();
        KeyboardActions.Register();
        TimingActions.Register();
        ScreenActions.Register();
        LogicActions.Register();
        AppActions.Register();
        TxtFileActions.Register();
    }
}
