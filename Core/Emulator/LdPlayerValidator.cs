using System.Diagnostics;
using System.IO;
using Macro.Localization;

namespace Macro.Core.Emulator;

/// <summary>
/// Validator for LDPlayer (LDPlayer 9, 5, 4, 3).
/// Inspects executables (dnplayer.exe, ldconsole.exe, dnconsole.exe), directory contents, and file permissions.
/// </summary>
public class LdPlayerValidator : IEmulatorValidator
{
    public string Id => "ldplayer";
    public string DisplayName => "LDPlayer";

    public string ExecutableFilter =>
        Loc.T("emu.filter.ldplayer", "LDPlayer (dnplayer.exe, ldconsole.exe)|dnplayer.exe;ldconsole.exe;dnconsole.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*");

    public string HelpHint =>
        Loc.T("emu.hint.ldplayer_dir", "Chọn thư mục cài đặt LDPlayer (ví dụ: C:\\LDPlayer\\LDPlayer9 hoặc D:\\leidian\\LDPlayer9).");

    public EmulatorValidationResult Validate(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return EmulatorValidationResult.Fail(
                Loc.T("emu.err.empty", "Đường dẫn không được để trống."));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath.Trim().Trim('"', '\''));
        }
        catch (Exception ex)
        {
            return EmulatorValidationResult.Fail(
                Loc.F("emu.err.invalid_path", "Đường dẫn không hợp lệ: {0}", ex.Message));
        }

        string dirPath;
        string exePath = null;

        if (Directory.Exists(fullPath))
        {
            var tools = LdPlayerService.ResolveTools(fullPath);
            if (tools.IsValid)
            {
                dirPath = tools.ToolDirectory;
                exePath = !string.IsNullOrEmpty(tools.DnPlayerPath) ? tools.DnPlayerPath : tools.LdConsolePath;
            }
            else
            {
                return EmulatorValidationResult.Fail(
                    Loc.T("emu.err.no_exe_in_dir",
                        "Thư mục được chọn không chứa tệp cần thiết của LDPlayer (dnplayer.exe hoặc ldconsole.exe).\nVui lòng chọn đúng thư mục cài đặt LDPlayer."));
            }
        }
        else if (File.Exists(fullPath))
        {
            dirPath = Path.GetDirectoryName(fullPath) ?? "";
            var fileName = Path.GetFileName(fullPath).ToLowerInvariant();
            if (fileName is "dnplayer.exe" or "ldconsole.exe" or "dnconsole.exe")
            {
                exePath = fullPath;
            }
            else
            {
                // Check if dnplayer.exe or ldconsole.exe exists in the same folder
                var dnplayerInDir = Path.Combine(dirPath, "dnplayer.exe");
                var ldconsoleInDir = Path.Combine(dirPath, "ldconsole.exe");
                var dnconsoleInDir = Path.Combine(dirPath, "dnconsole.exe");
                if (File.Exists(dnplayerInDir)) exePath = dnplayerInDir;
                else if (File.Exists(ldconsoleInDir)) exePath = ldconsoleInDir;
                else if (File.Exists(dnconsoleInDir)) exePath = dnconsoleInDir;
                else
                {
                    return EmulatorValidationResult.Fail(
                        Loc.T("emu.err.not_ldplayer",
                            "Thư mục chứa tệp này không thuộc trình giả lập LDPlayer hợp lệ. Không tìm thấy dnplayer.exe hoặc ldconsole.exe."));
                }
            }
        }
        else
        {
            return EmulatorValidationResult.Fail(
                Loc.F("emu.err.not_found", "Đường dẫn không tồn tại trên hệ thống:\n{0}", fullPath));
        }

        // Verify file accessibility and read permissions
        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0)
            {
                return EmulatorValidationResult.Fail(
                    Loc.T("emu.err.zero_byte", "Tệp thực thi bị rỗng (0 bytes). Vui lòng kiểm tra lại quá trình cài đặt LDPlayer."));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return EmulatorValidationResult.Fail(
                Loc.T("emu.err.no_permission", "Không có quyền truy cập tệp. Vui lòng kiểm tra quyền người dùng hoặc khởi chạy với quyền Administrator."));
        }
        catch (Exception ex)
        {
            return EmulatorValidationResult.Fail(
                Loc.F("emu.err.access_error", "Không thể truy cập tệp thực thi: {0}", ex.Message));
        }

        // Extract version information if available
        string version = "";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            version = info.ProductVersion ?? info.FileVersion ?? "";
        }
        catch { }

        return EmulatorValidationResult.Success("LDPlayer", exePath, dirPath, version);
    }
}
