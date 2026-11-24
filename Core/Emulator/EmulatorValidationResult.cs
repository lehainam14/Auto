namespace Macro.Core.Emulator;

/// <summary>
/// Detailed result of validating an emulator executable or directory path.
/// </summary>
public class EmulatorValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string DirectoryPath { get; set; } = "";
    public string EmulatorName { get; set; } = "";
    public string VersionInfo { get; set; } = "";

    public static EmulatorValidationResult Success(string emulatorName, string exePath, string dirPath, string version = "")
    {
        return new EmulatorValidationResult
        {
            IsValid = true,
            EmulatorName = emulatorName,
            ExecutablePath = exePath,
            DirectoryPath = dirPath,
            VersionInfo = version
        };
    }

    public static EmulatorValidationResult Fail(string error)
    {
        return new EmulatorValidationResult
        {
            IsValid = false,
            ErrorMessage = error
        };
    }
}
