namespace Macro.Core.Emulator;

/// <summary>
/// Extensible interface for validating Android emulator installations.
/// Allows adding support for other emulators (Nox, BlueStacks, MuMu...) in the future.
/// </summary>
public interface IEmulatorValidator
{
    string Id { get; }
    string DisplayName { get; }
    string ExecutableFilter { get; }
    string HelpHint { get; }
    EmulatorValidationResult Validate(string path);
}
