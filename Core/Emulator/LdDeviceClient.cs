using System.Diagnostics;
using System.IO;
using System.Text;
using Macro.Core.Screen;
using Macro.Core.Services;
using OpenCvSharp;

namespace Macro.Core.Emulator;

/// <summary>
/// Unified client communicating directly with a specific LDPlayer instance/device via ADB and CLI.
/// Guarantees that all screencaps, touches, swipes, typing, key events, and app launches
/// are executed strictly inside the designated Android emulator, with zero PC desktop interaction.
/// </summary>
public class LdDeviceClient
{
    public int TabIndex { get; }
    public string DeviceSerial => $"127.0.0.1:{5555 + TabIndex * 2}";
    public string EmulatorSerial => $"emulator-{5554 + TabIndex * 2}";

    public LdDeviceClient(int tabIndex)
    {
        TabIndex = Math.Max(0, tabIndex);
    }

    /// <summary>
    /// Returns the effective active ADB serial for this tab (either 127.0.0.1:port or emulator-port).
    /// </summary>
    public async Task<string> GetActiveAdbSerialAsync(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath) || !File.Exists(adbPath)) return DeviceSerial;

        // Try deviceSerial first
        _ = await LdPlayerService.RunCliCommandAsync(adbPath, $"connect {DeviceSerial}", 1500);
        var (exit1, out1, _) = await LdPlayerService.RunCliCommandAsync(adbPath, $"-s {DeviceSerial} get-state", 1500);
        if (exit1 == 0 && out1.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
            return DeviceSerial;

        // Try emulatorSerial
        var (exit2, out2, _) = await LdPlayerService.RunCliCommandAsync(adbPath, $"-s {EmulatorSerial} get-state", 1500);
        if (exit2 == 0 && out2.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
            return EmulatorSerial;

        return DeviceSerial;
    }

    /// <summary>
    /// Checks whether this specific LDPlayer instance is currently running and accepting ADB/CLI commands.
    /// Works regardless of whether the emulator window is shown, hidden, minimized, occluded, or headless.
    /// </summary>
    public async Task<bool> IsOnlineAsync()
    {
        var tools = LdPlayerService.ResolveTools(EmulatorManager.CurrentPath);
        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            _ = await LdPlayerService.RunCliCommandAsync(tools.AdbPath, $"connect {DeviceSerial}", 1200);
            var (exit1, out1, _) = await LdPlayerService.RunCliCommandAsync(tools.AdbPath, $"-s {DeviceSerial} get-state", 1500);
            if (exit1 == 0 && out1.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                return true;

            var (exit2, out2, _) = await LdPlayerService.RunCliCommandAsync(tools.AdbPath, $"-s {EmulatorSerial} get-state", 1500);
            if (exit2 == 0 && out2.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (tools.HasConsole && File.Exists(tools.LdConsolePath))
        {
            var (exit, output, _) = await LdPlayerService.RunCliCommandAsync(tools.LdConsolePath, $"isrunning --index {TabIndex}", 2000);
            if (output.Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Captures the Android screen directly from this LDPlayer instance's framebuffer into an OpenCV Mat.
    /// Never captures the Windows PC desktop, PC windows, or desktop screen coordinates.
    /// Saves a debug snapshot to captures/debug_ldplayer_tab{TabIndex}.png for inspection.
    /// </summary>
    public async Task<TabCaptureMatResult> CaptureFrameAsync(bool saveDebugSnapshot = true)
    {
        var console = EmulatorManager.GetLdConsolePath();
        var tools = LdPlayerService.ResolveTools(Path.GetDirectoryName(console) ?? EmulatorManager.CurrentPath);

        // Ensure adb debugging is enabled in configuration
        LdPlayerService.EnsureAdbConfig(tools.ToolDirectory, TabIndex);

        var tempFolder = ScreenshotService.CapturesFolder();
        if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
        string debugPath = Path.Combine(tempFolder, $"debug_ldplayer_tab{TabIndex}.png");

        // 1. Primary: Direct ADB exec-out screencap -p
        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            try
            {
                string activeSerial = await GetActiveAdbSerialAsync(tools.AdbPath);
                var (capExit, pngBytes, capErr) = await LdPlayerService.RunCliCommandBytesAsync(
                    tools.AdbPath, $"-s {activeSerial} exec-out screencap -p", 4000);

                if (capExit == 0 && pngBytes != null && pngBytes.Length > 100 && LdPlayerService.IsPngHeader(pngBytes))
                {
                    if (saveDebugSnapshot)
                    {
                        try { await File.WriteAllBytesAsync(debugPath, pngBytes); } catch { }
                    }

                    var mat = Cv2.ImDecode(pngBytes, ImreadModes.Color);
                    if (mat != null && mat.Width > 10 && mat.Height > 10)
                    {
                        return new TabCaptureMatResult
                        {
                            Success = true,
                            Frame = mat,
                            DevW = mat.Width,
                            DevH = mat.Height,
                            ErrorMessage = ""
                        };
                    }
                }
            }
            catch { }
        }

        // 2. Secondary: ADB shell screencap -p to /sdcard + adb pull
        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            try
            {
                string activeSerial = await GetActiveAdbSerialAsync(tools.AdbPath);
                string remotePath = $"/sdcard/_bg_cap_{TabIndex}.png";
                var (cExit, _, _) = await LdPlayerService.RunCliCommandAsync(
                    tools.AdbPath, $"-s {activeSerial} shell screencap -p {remotePath}", 5000);

                if (cExit == 0)
                {
                    var (pExit, _, _) = await LdPlayerService.RunCliCommandAsync(
                        tools.AdbPath, $"-s {activeSerial} pull {remotePath} \"{debugPath}\"", 5000);

                    if (File.Exists(debugPath) && new FileInfo(debugPath).Length > 100)
                    {
                        var mat = Cv2.ImRead(debugPath, ImreadModes.Color);
                        if (mat != null && mat.Width > 10 && mat.Height > 10)
                        {
                            return new TabCaptureMatResult
                            {
                                Success = true,
                                Frame = mat,
                                DevW = mat.Width,
                                DevH = mat.Height,
                                ErrorMessage = ""
                            };
                        }
                    }
                }
            }
            catch { }
        }

        // 3. Tertiary: ldconsole adb shell screencap + pull
        if (tools.HasConsole && File.Exists(tools.LdConsolePath))
        {
            try
            {
                string remotePath = $"/sdcard/_bg_cap_{TabIndex}.png";
                var (cExit, _, cErr) = await LdPlayerService.RunCliCommandAsync(
                    tools.LdConsolePath, $"adb --index {TabIndex} --command \"shell screencap -p {remotePath}\"", 6000);

                if (cExit == 0)
                {
                    var (pExit, _, _) = await LdPlayerService.RunCliCommandAsync(
                        tools.LdConsolePath, $"pull --index {TabIndex} --remote {remotePath} --local \"{debugPath}\"", 6000);

                    if (File.Exists(debugPath) && new FileInfo(debugPath).Length > 100)
                    {
                        var mat = Cv2.ImRead(debugPath, ImreadModes.Color);
                        if (mat != null && mat.Width > 10 && mat.Height > 10)
                        {
                            return new TabCaptureMatResult
                            {
                                Success = true,
                                Frame = mat,
                                DevW = mat.Width,
                                DevH = mat.Height,
                                ErrorMessage = ""
                            };
                        }
                    }
                }
            }
            catch { }
        }

        return new TabCaptureMatResult
        {
            Success = false,
            ErrorMessage = $"Không thể chụp màn hình trực tiếp từ Tab #{TabIndex}. Vui lòng kiểm tra tab đang chạy và kết nối ADB."
        };
    }

    public TabCaptureMatResult CaptureFrame(bool saveDebugSnapshot = true) =>
        CaptureFrameAsync(saveDebugSnapshot).GetAwaiter().GetResult();

    /// <summary>
    /// Captures the Android screen directly to a local PNG file on disk.
    /// </summary>
    public bool CaptureToDisk(string localFilePath)
    {
        var result = CaptureFrame(saveDebugSnapshot: false);
        if (!result.Success || result.Frame == null) return false;
        try
        {
            using (result.Frame)
            {
                var dir = Path.GetDirectoryName(localFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Cv2.ImWrite(localFilePath, result.Frame);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends background click/tap to this LDPlayer instance at Android coordinates (x, y).
    /// </summary>
    public bool Tap(int x, int y, string button = "left", int clicks = 1)
    {
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return false;

        if (button == "right")
        {
            var (exit, _, _) = LdPlayerService.RunCliCommandAsync(
                console, $"adb --index {TabIndex} --command \"shell input keyevent 4\"", 4000).GetAwaiter().GetResult();
            return exit == 0;
        }

        for (int i = 0; i < Math.Max(1, clicks); i++)
        {
            var (exit, _, _) = LdPlayerService.RunCliCommandAsync(
                console, $"adb --index {TabIndex} --command \"shell input tap {x} {y}\"", 4000).GetAwaiter().GetResult();
            if (exit != 0) return false;
            if (i < clicks - 1) Thread.Sleep(100);
        }
        return true;
    }

    /// <summary>
    /// Executes a background swipe/drag gesture on this LDPlayer instance.
    /// </summary>
    public bool Swipe(int x1, int y1, int x2, int y2, int durationMs = 300)
    {
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return false;

        int dur = Math.Max(100, durationMs);
        var (exit, _, _) = LdPlayerService.RunCliCommandAsync(
            console, $"adb --index {TabIndex} --command \"shell input swipe {x1} {y1} {x2} {y2} {dur}\"", 5000 + dur).GetAwaiter().GetResult();
        return exit == 0;
    }

    /// <summary>
    /// Executes a background scroll gesture on this LDPlayer instance by swiping.
    /// </summary>
    public bool Scroll(int x, int y, int amount)
    {
        if (amount == 0) return false;
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return false;

        int distance = Math.Clamp(Math.Abs(amount) * 80, 150, 600);
        int targetY = amount > 0 ? y + distance : y - distance;
        int dur = 250;

        var (exit, _, _) = LdPlayerService.RunCliCommandAsync(
            console, $"adb --index {TabIndex} --command \"shell input swipe {x} {y} {x} {targetY} {dur}\"", 4000).GetAwaiter().GetResult();
        return exit == 0;
    }

    /// <summary>
    /// Sends text into the active Android input field on this LDPlayer instance in the background.
    /// Selects a single communication branch (direct_adb or ldconsole_adb) and guarantees
    /// that content is sent strictly once with no duplicate calls or retry duplications.
    /// </summary>
    public TypeTextResult TypeTextDetailed(string text, Action<string> log = null)
    {
        if (string.IsNullOrEmpty(text))
            return new TypeTextResult { Success = true, Branch = "none (empty)", LinesCount = 0 };

        var console = EmulatorManager.GetLdConsolePath();
        var tools = LdPlayerService.ResolveTools(Path.GetDirectoryName(console) ?? EmulatorManager.CurrentPath);

        string branch = "ldconsole_adb";
        string targetSerial = null;
        bool useDirectAdb = false;

        // Determine if direct ADB is usable for this tab before dispatching
        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            try
            {
                int adbPort = 5555 + TabIndex * 2;
                string devSerial = $"127.0.0.1:{adbPort}";
                _ = LdPlayerService.RunCliCommandAsync(tools.AdbPath, $"connect {devSerial}", 1200).GetAwaiter().GetResult();
                var (exit1, out1, _) = LdPlayerService.RunCliCommandAsync(tools.AdbPath, $"-s {devSerial} get-state", 1200).GetAwaiter().GetResult();
                if (exit1 == 0 && out1.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                {
                    useDirectAdb = true;
                    targetSerial = devSerial;
                    branch = "direct_adb";
                }
                else
                {
                    int emuPort = 5554 + TabIndex * 2;
                    string emuSerial = $"emulator-{emuPort}";
                    var (exit2, out2, _) = LdPlayerService.RunCliCommandAsync(tools.AdbPath, $"-s {emuSerial} get-state", 1200).GetAwaiter().GetResult();
                    if (exit2 == 0 && out2.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                    {
                        useDirectAdb = true;
                        targetSerial = emuSerial;
                        branch = "direct_adb";
                    }
                }
            }
            catch
            {
                useDirectAdb = false;
            }
        }

        if (!useDirectAdb && (string.IsNullOrEmpty(console) || !File.Exists(console)))
        {
            return new TypeTextResult
            {
                Success = false,
                Branch = "none",
                Error = "Không tìm thấy ldconsole.exe hoặc adb.exe để gửi văn bản."
            };
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > 0)
            {
                var escaped = LdPlayerService.FormatAdbInputText(line);
                int exit;
                string err;

                if (useDirectAdb && !string.IsNullOrEmpty(targetSerial))
                {
                    var (code, _, eStr) = LdPlayerService.RunCliCommandAsync(
                        tools.AdbPath, $"-s {targetSerial} shell input text \"{escaped}\"", 6000).GetAwaiter().GetResult();
                    exit = code;
                    err = eStr;
                }
                else
                {
                    var (code, _, eStr) = LdPlayerService.RunCliCommandAsync(
                        console, $"adb --index {TabIndex} --command \"shell input text \\\"{escaped}\\\"\"", 6000).GetAwaiter().GetResult();
                    exit = code;
                    err = eStr;
                }

                if (exit != 0)
                {
                    // CRITICAL GUARD: The command was already dispatched to the process/device.
                    // DO NOT retry or fall back to another branch, to avoid typing duplicates!
                    return new TypeTextResult
                    {
                        Success = false,
                        Branch = branch,
                        Error = $"Lệnh gửi dòng {i + 1} thất bại (ExitCode: {exit}, Err: {err}). Không thử lại để tránh nhập trùng.",
                        LinesCount = i
                    };
                }
            }

            if (i < lines.Length - 1)
            {
                int enterExit;
                string enterErr;
                if (useDirectAdb && !string.IsNullOrEmpty(targetSerial))
                {
                    var (code, _, eStr) = LdPlayerService.RunCliCommandAsync(
                        tools.AdbPath, $"-s {targetSerial} shell input keyevent 66", 3000).GetAwaiter().GetResult();
                    enterExit = code;
                    enterErr = eStr;
                }
                else
                {
                    var (code, _, eStr) = LdPlayerService.RunCliCommandAsync(
                        console, $"adb --index {TabIndex} --command \"shell input keyevent 66\"", 3000).GetAwaiter().GetResult();
                    enterExit = code;
                    enterErr = eStr;
                }

                if (enterExit != 0)
                {
                    return new TypeTextResult
                    {
                        Success = false,
                        Branch = branch,
                        Error = $"Gửi phím xuống dòng sau dòng {i + 1} thất bại (ExitCode: {enterExit}, Err: {enterErr}).",
                        LinesCount = i + 1
                    };
                }
            }
        }

        return new TypeTextResult
        {
            Success = true,
            Branch = branch,
            LinesCount = lines.Length
        };
    }

    /// <summary>
    /// Sends text into the active Android input field on this LDPlayer instance in the background.
    /// </summary>
    public bool TypeText(string text) => TypeTextDetailed(text).Success;

    /// <summary>
    /// Sends keypress to this LDPlayer instance in the background.
    /// </summary>
    public bool PressKey(string key, int repeat = 1, int intervalMs = 50)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return false;

        int keyCode = LdPlayerService.MapKeyToAndroidKeyCode(key);
        if (keyCode <= 0) return false;

        for (int i = 0; i < Math.Max(1, repeat); i++)
        {
            var (exit, _, _) = LdPlayerService.RunCliCommandAsync(
                console, $"adb --index {TabIndex} --command \"shell input keyevent {keyCode}\"", 3000).GetAwaiter().GetResult();
            if (exit != 0) return false;
            if (i < repeat - 1 && intervalMs > 0) Thread.Sleep(intervalMs);
        }
        return true;
    }

    /// <summary>
    /// Sends hotkey combination to this LDPlayer instance.
    /// </summary>
    public bool PressHotkey(string[] modifiers, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return false;

        int keyCode = LdPlayerService.MapKeyToAndroidKeyCode(key);
        if (keyCode <= 0) return false;

        // In Android ADB, keyevent supports --meta <metastate>
        // META_CTRL_ON = 4096 (0x1000), META_SHIFT_ON = 1, META_ALT_ON = 2
        int metaState = 0;
        if (modifiers != null)
        {
            foreach (var m in modifiers)
            {
                var ml = m.ToLowerInvariant();
                if (ml.Contains("ctrl")) metaState |= 4096;
                if (ml.Contains("shift")) metaState |= 1;
                if (ml.Contains("alt")) metaState |= 2;
            }
        }

        string cmd = metaState > 0
            ? $"shell input keyevent --meta {metaState} {keyCode}"
            : $"shell input keyevent {keyCode}";

        var (exit, _, _) = LdPlayerService.RunCliCommandAsync(
            console, $"adb --index {TabIndex} --command \"{cmd}\"", 3000).GetAwaiter().GetResult();
        return exit == 0;
    }

    /// <summary>
    /// Launches an Android app on this LDPlayer instance.
    /// </summary>
    public async Task<bool> LaunchAppAsync(string packageName)
    {
        var console = EmulatorManager.GetLdConsolePath();
        return await LdPlayerService.LaunchAppAsync(console, TabIndex, packageName);
    }
}

/// <summary>
/// Result of sending text to an LDPlayer tab.
/// </summary>
public struct TypeTextResult
{
    public bool Success;
    public string Branch;
    public string Error;
    public int LinesCount;
}

