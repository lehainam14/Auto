using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Macro.Core;
using Macro.Core.Input;
using Macro.Core.Screen;
using Macro.Core.Services;
using OpenCvSharp;

namespace Macro.Core.Emulator;

/// <summary>
/// An assigned macro item bound to an LDPlayer instance.
/// </summary>
public class AssignedMacroItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _filePath = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
    }

    public string FilePath
    {
        get => _filePath;
        set { if (_filePath != value) { _filePath = value; OnPropertyChanged(nameof(FilePath)); } }
    }

    public Workflow Workflow { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Information about an LDPlayer instance/tab.
/// </summary>
public class LdInstanceInfo : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isRunning;
    private string _operationStatus = "Sẵn sàng";
    private string _name = "";

    public int Index { get; set; }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
    }

    public IntPtr TopHwnd { get; set; }
    public IntPtr BindHwnd { get; set; }
    public int Pid { get; set; }
    public int VBoxPid { get; set; }
    public int ScreenWidth { get; set; } = 960;
    public int ScreenHeight { get; set; } = 540;
    public int Dpi { get; set; } = 160;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string OperationStatus
    {
        get => _operationStatus;
        set { if (_operationStatus != value) { _operationStatus = value; OnPropertyChanged(nameof(OperationStatus)); } }
    }

    public string StatusText => IsRunning ? "Đang chạy" : "Đã tắt";

    public ObservableCollection<AssignedMacroItem> AssignedMacros { get; } = new();
    public int RepeatCount { get; set; } = 1;

    /// <summary>
    /// Attempts to assign a macro to this instance.
    /// Returns false if a macro with the same name or file path is already assigned to this tab.
    /// </summary>
    public bool TryAddMacro(string name, string filePath, Workflow workflow, out string reason)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            reason = "Tên macro không hợp lệ.";
            return false;
        }

        bool duplicate = AssignedMacros.Any(m =>
            (!string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(m.FilePath) &&
             string.Equals(Path.GetFullPath(m.FilePath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(m.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            reason = $"Tab #{Index} ({Name}) đã được gán macro '{name}' trước đó.";
            return false;
        }

        var clonedWorkflow = workflow != null ? workflow.Clone() : null;
        if (clonedWorkflow != null)
        {
            clonedWorkflow.BindToTab(Index);
        }

        AssignedMacros.Add(new AssignedMacroItem
        {
            Name = name.Trim(),
            FilePath = filePath ?? "",
            Workflow = clonedWorkflow
        });

        reason = "";
        OnPropertyChanged(nameof(AssignedMacros));
        return true;
    }

    /// <summary>
    /// Removes an assigned macro by Id from this tab only, without affecting other tabs.
    /// </summary>
    public bool RemoveMacro(string macroId)
    {
        var item = AssignedMacros.FirstOrDefault(m => m.Id == macroId);
        if (item != null)
        {
            AssignedMacros.Remove(item);
            OnPropertyChanged(nameof(AssignedMacros));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes all assigned macros from this tab only.
    /// </summary>
    public void ClearMacros()
    {
        AssignedMacros.Clear();
        OnPropertyChanged(nameof(AssignedMacros));
    }

    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Settings for launching instances in sequence.
/// </summary>
public class LdLaunchOptions
{
    public int IntervalSeconds { get; set; } = 5;       // Khoảng chờ giữa mỗi tab (giây)
    public int MaxWaitSeconds { get; set; } = 45;       // Thời gian tối đa chờ mỗi tab khởi động (giây)
    public int MaxRetries { get; set; } = 1;            // Số lần thử lại nếu quá thời gian
}

/// <summary>
/// Resolved tool executables in an LDPlayer installation directory.
/// </summary>
public class LdPlayerTools
{
    public bool IsValid => (!string.IsNullOrEmpty(LdConsolePath) && File.Exists(LdConsolePath))
                        || (!string.IsNullOrEmpty(DnPlayerPath) && File.Exists(DnPlayerPath));
    public bool HasConsole => !string.IsNullOrEmpty(LdConsolePath) && File.Exists(LdConsolePath);
    public bool HasAdb => !string.IsNullOrEmpty(AdbPath) && File.Exists(AdbPath);
    public string LdConsolePath { get; set; } = "";
    public string DnPlayerPath { get; set; } = "";
    public string AdbPath { get; set; } = "";
    public string ToolDirectory { get; set; } = "";
    public string RootDirectory { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

/// <summary>
/// Decoupled service for discovering LDPlayer tools, querying instances,
/// and executing instance control commands (list, launch, quit, sequential startup).
/// </summary>
public static class LdPlayerService
{
    /// <summary>
    /// Dynamically discovers LDPlayer tools (ldconsole.exe / dnconsole.exe, dnplayer.exe)
    /// from a base directory without assuming fixed subfolder names.
    /// </summary>
    public static LdPlayerTools ResolveTools(string baseFolder)
    {
        var tools = new LdPlayerTools { RootDirectory = baseFolder ?? "" };
        if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
        {
            tools.ErrorMessage = string.IsNullOrWhiteSpace(baseFolder)
                ? "Chưa chọn thư mục cài đặt LDPlayer."
                : "Thư mục không tồn tại: " + baseFolder;
            return tools;
        }

        // 1. Direct check in baseFolder
        if (CheckDir(baseFolder, tools)) return tools;

        // 2. Check pathconfig.ini if present (e.g. in C:\LDPlayer\ldmutiplayer\pathconfig.ini)
        try
        {
            var iniPath = Path.Combine(baseFolder, "ldmutiplayer", "pathconfig.ini");
            if (File.Exists(iniPath))
            {
                foreach (var line in File.ReadAllLines(iniPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("player", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                    {
                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var targetDir = parts[1].Trim().TrimEnd('\\', '/');
                            if (Directory.Exists(targetDir) && CheckDir(targetDir, tools))
                                return tools;
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Check candidate subfolders (e.g. LDPlayer9, LDPlayer5, LDPlayer4, etc.)
        try
        {
            var subdirs = Directory.GetDirectories(baseFolder);
            // Sort in descending order to prefer higher versions (LDPlayer9 > LDPlayer4)
            var sorted = subdirs.OrderByDescending(d => Path.GetFileName(d));
            foreach (var sub in sorted)
            {
                if (CheckDir(sub, tools)) return tools;
            }

            // 4. Search up to 2 levels deep if not yet found
            foreach (var sub in subdirs)
            {
                try
                {
                    foreach (var sub2 in Directory.GetDirectories(sub))
                    {
                        if (CheckDir(sub2, tools)) return tools;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            tools.ErrorMessage = "Lỗi khi quét thư mục LDPlayer: " + ex.Message;
            return tools;
        }

        tools.ErrorMessage = "Không tìm thấy ldconsole.exe hoặc dnplayer.exe trong thư mục: " + baseFolder;
        return tools;

        static bool CheckDir(string dir, LdPlayerTools t)
        {
            var ldconsole = Path.Combine(dir, "ldconsole.exe");
            var dnconsole = Path.Combine(dir, "dnconsole.exe");
            var dnplayer = Path.Combine(dir, "dnplayer.exe");
            var consoleExe = File.Exists(ldconsole) ? ldconsole : (File.Exists(dnconsole) ? dnconsole : null);

            if (consoleExe != null || File.Exists(dnplayer))
            {
                t.LdConsolePath = consoleExe ?? "";
                t.ToolDirectory = dir;
                if (File.Exists(dnplayer)) t.DnPlayerPath = dnplayer;
                var adb = Path.Combine(dir, "adb.exe");
                if (File.Exists(adb)) t.AdbPath = adb;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Executes an LDPlayer CLI command asynchronously with timeout and cancellation.
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunCliCommandAsync(
        string exePath,
        string arguments,
        int timeoutMs = 10000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return (-1, "", "Executable not found: " + exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
        };

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return (-1, outputBuilder.ToString().Trim(), "Command timed out or cancelled");
        }
        catch (Exception ex)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return (-1, "", ex.Message);
        }
    }

    /// <summary>
    /// Executes a process and reads its stdout as a raw binary byte stream.
    /// Useful for streaming PNG screencaps directly into memory without disk roundtrips.
    /// </summary>
    public static async Task<(int ExitCode, byte[] Bytes, string Error)> RunCliCommandBytesAsync(
        string exePath,
        string arguments,
        int timeoutMs = 8000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return (-1, Array.Empty<byte>(), "Executable not found: " + exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
        };

        using var process = new Process { StartInfo = psi };
        using var ms = new MemoryStream();

        try
        {
            process.Start();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var readStdoutTask = process.StandardOutput.BaseStream.CopyToAsync(ms, cts.Token);
            var readStderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await Task.WhenAll(readStdoutTask, readStderrTask);
            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode, ms.ToArray(), (await readStderrTask).Trim());
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return (-1, Array.Empty<byte>(), "Command timed out or cancelled");
        }
        catch (Exception ex)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            return (-1, Array.Empty<byte>(), ex.Message);
        }
    }

    public static bool IsPngHeader(byte[] bytes) =>
        bytes != null && bytes.Length >= 8 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static readonly Dictionary<int, LdInstanceInfo> _liveInstances = new();
    public static IReadOnlyDictionary<int, LdInstanceInfo> LiveInstances => _liveInstances;

    public static void RegisterLiveInstance(LdInstanceInfo instance)
    {
        if (instance != null)
        {
            lock (_liveInstances)
            {
                _liveInstances[instance.Index] = instance;
            }
        }
    }

    /// <summary>
    /// Parses the raw output of 'ldconsole list2' into strongly-typed LdInstanceInfo objects.
    /// Format per line: index,name,top_hwnd,bind_hwnd,android_started,pid,vbox_pid[,width,height,dpi]
    /// </summary>
    public static List<LdInstanceInfo> ParseList2Output(string rawOutput)
    {
        var list = new List<LdInstanceInfo>();
        if (string.IsNullOrWhiteSpace(rawOutput)) return list;

        var lines = rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            var parts = trimmed.Split(',');
            if (parts.Length < 2) continue;

            if (int.TryParse(parts[0].Trim(), out var index))
            {
                var name = parts[1].Trim();
                long topHwnd = 0;
                long bindHwnd = 0;
                bool isRunning = false;
                int pid = -1;
                int vboxPid = -1;
                int screenWidth = 960;
                int screenHeight = 540;
                int dpi = 160;

                if (parts.Length > 2) long.TryParse(parts[2].Trim(), out topHwnd);
                if (parts.Length > 3) long.TryParse(parts[3].Trim(), out bindHwnd);
                if (parts.Length > 4) isRunning = parts[4].Trim() == "1";
                if (parts.Length > 5) int.TryParse(parts[5].Trim(), out pid);
                if (parts.Length > 6) int.TryParse(parts[6].Trim(), out vboxPid);
                if (parts.Length > 7 && int.TryParse(parts[7].Trim(), out var sw) && sw > 0) screenWidth = sw;
                if (parts.Length > 8 && int.TryParse(parts[8].Trim(), out var sh) && sh > 0) screenHeight = sh;
                if (parts.Length > 9 && int.TryParse(parts[9].Trim(), out var d) && d > 0) dpi = d;

                var inst = new LdInstanceInfo
                {
                    Index = index,
                    Name = name,
                    TopHwnd = (IntPtr)topHwnd,
                    BindHwnd = (IntPtr)bindHwnd,
                    IsRunning = isRunning,
                    Pid = pid,
                    VBoxPid = vboxPid,
                    ScreenWidth = screenWidth,
                    ScreenHeight = screenHeight,
                    Dpi = dpi,
                    OperationStatus = isRunning ? "Đang chạy" : "Đã tắt"
                };

                list.Add(inst);
                lock (_liveInstances)
                {
                    _liveInstances[index] = inst;
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Calculates the rendered content area and letterbox/pillarbox border spacing
    /// when mapping a device screen of aspect ratio (devW / devH) into a container of size (containerW, containerH).
    /// </summary>
    public static (int BorderX, int BorderY, int ContentW, int ContentH) CalculateContentBounds(
        int containerW, int containerH,
        int devW, int devH)
    {
        if (devW <= 0 || devH <= 0 || containerW <= 0 || containerH <= 0)
            return (0, 0, Math.Max(0, containerW), Math.Max(0, containerH));

        double arDev = (double)devW / devH;
        double arCont = (double)containerW / containerH;

        if (Math.Abs(arCont - arDev) < 0.005)
        {
            return (0, 0, containerW, containerH);
        }
        else if (arCont > arDev)
        {
            // Container is wider than device (pillarbox: black borders left & right)
            int contentH = containerH;
            int contentW = (int)Math.Round(containerH * arDev);
            int borderX = Math.Max(0, (containerW - contentW) / 2);
            return (borderX, 0, contentW, contentH);
        }
        else
        {
            // Container is taller than device (letterbox: black borders top & bottom)
            int contentW = containerW;
            int contentH = (int)Math.Round(containerW / arDev);
            int borderY = Math.Max(0, (containerH - contentH) / 2);
            return (0, borderY, contentW, contentH);
        }
    }

    /// <summary>
    /// Translates a point (imgX, imgY) from preview image pixels into the tab's internal device coordinate system (0..devW-1, 0..devH-1).
    /// Handles zoom, scaling, and pillarbox/letterbox border margins accurately.
    /// </summary>
    public static (int TabX, int TabY) TranslateImageToTab(
        int imgW, int imgH,
        int devW, int devH,
        double imgX, double imgY)
    {
        if (devW <= 0) devW = imgW > 0 ? imgW : 960;
        if (devH <= 0) devH = imgH > 0 ? imgH : 540;

        var (bx, by, cw, ch) = CalculateContentBounds(imgW, imgH, devW, devH);
        if (cw <= 0) cw = imgW;
        if (ch <= 0) ch = imgH;

        double relX = imgX - bx;
        double relY = imgY - by;

        int tabX = (int)Math.Round((relX / cw) * devW);
        int tabY = (int)Math.Round((relY / ch) * devH);

        tabX = Math.Clamp(tabX, 0, devW - 1);
        tabY = Math.Clamp(tabY, 0, devH - 1);

        return (tabX, tabY);
    }

    /// <summary>
    /// Finds the active Win32 window handle for an LDPlayer instance/tab.
    /// Prefers BindHwnd (the inner Android render viewport) so coordinates map directly to Android,
    /// and provides topHwnd for bringing the window to the foreground.
    /// </summary>
    public static IntPtr FindTabWindowHandle(int tabIndex, out IntPtr topHwnd, out LdInstanceInfo instance)
    {
        topHwnd = IntPtr.Zero;
        instance = null;

        lock (_liveInstances)
        {
            if (_liveInstances.TryGetValue(tabIndex, out var inst))
                instance = inst;
        }

        if (instance == null && EmulatorManager.SelectedInstance != null && EmulatorManager.SelectedInstance.Index == tabIndex)
        {
            instance = EmulatorManager.SelectedInstance;
        }

        IntPtr targetHwnd = IntPtr.Zero;

        // 1. Try BindHwnd if valid and has positive dimensions
        if (instance != null && instance.BindHwnd != IntPtr.Zero && IsWindow(instance.BindHwnd))
        {
            GetWindowRect(instance.BindHwnd, out RECT r);
            if (r.Right - r.Left > 50 && r.Bottom - r.Top > 50)
            {
                targetHwnd = instance.BindHwnd;
                topHwnd = instance.TopHwnd != IntPtr.Zero && IsWindow(instance.TopHwnd)
                    ? instance.TopHwnd
                    : targetHwnd;
                return targetHwnd;
            }
        }

        // 2. Try TopHwnd
        if (instance != null && instance.TopHwnd != IntPtr.Zero && IsWindow(instance.TopHwnd))
        {
            topHwnd = instance.TopHwnd;
            targetHwnd = instance.TopHwnd;
            return targetHwnd;
        }

        // 3. Try finding window by PID
        if (instance != null && instance.Pid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(instance.Pid);
                if (proc.MainWindowHandle != IntPtr.Zero && IsWindow(proc.MainWindowHandle))
                {
                    topHwnd = proc.MainWindowHandle;
                    targetHwnd = proc.MainWindowHandle;
                    return targetHwnd;
                }
            }
            catch { }
        }

        // 4. Try finding window by title
        if (instance != null && !string.IsNullOrEmpty(instance.Name))
        {
            var h = FindWindow(null, instance.Name);
            if (h != IntPtr.Zero && IsWindow(h))
            {
                topHwnd = h;
                targetHwnd = h;
                return targetHwnd;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Translates internal tab coordinates (tabX, tabY) to current desktop screen coordinates at runtime.
    /// Dynamically tracks the tab window if it is moved or resized by the user on the desktop.
    /// If tab is not running or window is not found, returns null so caller can fall back to raw coordinates.
    /// </summary>
    public static (int ScreenX, int ScreenY)? TranslateTabToScreen(int tabIndex, int tabX, int tabY)
    {
        if (tabIndex < 0) return null;

        var targetHwnd = FindTabWindowHandle(tabIndex, out var topHwnd, out var inst);
        if (targetHwnd == IntPtr.Zero || !IsWindow(targetHwnd))
            return null;

        GetWindowRect(targetHwnd, out RECT rect);
        int winW = rect.Right - rect.Left;
        int winH = rect.Bottom - rect.Top;
        if (winW <= 20 || winH <= 20)
            return null;

        // Restore if minimized and bring to foreground so clicks register properly
        var activateHwnd = topHwnd != IntPtr.Zero ? topHwnd : targetHwnd;
        if (IsIconic(activateHwnd)) ShowWindow(activateHwnd, 9); // SW_RESTORE
        SetForegroundWindow(activateHwnd);

        int devW = inst?.ScreenWidth > 0 ? inst.ScreenWidth : 960;
        int devH = inst?.ScreenHeight > 0 ? inst.ScreenHeight : 540;

        var (bx, by, cw, ch) = CalculateContentBounds(winW, winH, devW, devH);
        int localX = bx + (int)Math.Round(((double)tabX / devW) * cw);
        int localY = by + (int)Math.Round(((double)tabY / devH) * ch);

        int screenX = rect.Left + localX;
        int screenY = rect.Top + localY;

        return (screenX, screenY);
    }

    /// <summary>
    /// Gets the desktop screen bounding box (X, Y, W, H) for the specified tab instance.
    /// </summary>
    public static (int X, int Y, int W, int H)? GetTabWindowBounds(int tabIndex)
    {
        if (tabIndex < 0) return null;
        var targetHwnd = FindTabWindowHandle(tabIndex, out _, out _);
        if (targetHwnd == IntPtr.Zero || !IsWindow(targetHwnd)) return null;

        GetWindowRect(targetHwnd, out RECT rect);
        int winW = rect.Right - rect.Left;
        int winH = rect.Bottom - rect.Top;
        if (winW <= 20 || winH <= 20) return null;

        return (rect.Left, rect.Top, winW, winH);
    }

    /// <summary>
    /// Retrieves the list of instances and their real-time running states via 'ldconsole list2'.
    /// </summary>
    public static async Task<(List<LdInstanceInfo> Instances, string ErrorMessage)> GetInstancesAsync(string ldConsolePath)
    {
        if (string.IsNullOrEmpty(ldConsolePath) || !File.Exists(ldConsolePath))
            return (new List<LdInstanceInfo>(), "Không tìm thấy tệp ldconsole.exe tại đường dẫn cấu hình.");

        var (exitCode, output, error) = await RunCliCommandAsync(ldConsolePath, "list2", 8000);
        if (exitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            return (new List<LdInstanceInfo>(), string.IsNullOrWhiteSpace(error) ? "Không thể thực thi lệnh 'ldconsole list2'." : error);
        }

        var instances = ParseList2Output(output);
        return (instances, null);
    }

    /// <summary>
    /// Checks if a specific instance index is running via ADB device state or ldconsole isrunning.
    /// Accurately detects running instances even if minimized, hidden, occluded, or headless.
    /// </summary>
    public static async Task<bool> IsRunningAsync(string ldConsolePath, int index)
    {
        // 1. Primary: If ADB is responding, the instance is actively running in Android
        var tools = ResolveTools(Path.GetDirectoryName(ldConsolePath) ?? EmulatorManager.CurrentPath);
        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            try
            {
                int adbPort = 5555 + index * 2;
                string devSerial = $"127.0.0.1:{adbPort}";
                _ = await RunCliCommandAsync(tools.AdbPath, $"connect {devSerial}", 1200);
                var (exit1, out1, _) = await RunCliCommandAsync(tools.AdbPath, $"-s {devSerial} get-state", 1500);
                if (exit1 == 0 && out1.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                    return true;

                int emuPort = 5554 + index * 2;
                var (exit2, out2, _) = await RunCliCommandAsync(tools.AdbPath, $"-s emulator-{emuPort} get-state", 1500);
                if (exit2 == 0 && out2.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }

        // 2. Secondary: ldconsole isrunning
        if (!string.IsNullOrEmpty(ldConsolePath) && File.Exists(ldConsolePath))
        {
            var (exitCode, output, _) = await RunCliCommandAsync(ldConsolePath, $"isrunning --index {index}", 2500);
            if (output.Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 3. Tertiary: check live registered instances
        lock (_liveInstances)
        {
            if (_liveInstances.TryGetValue(index, out var inst) && inst.IsRunning)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Launches an instance via 'ldconsole launch --index {index}'.
    /// </summary>
    public static async Task<bool> LaunchInstanceAsync(string ldConsolePath, int index)
    {
        var (exitCode, _, _) = await RunCliCommandAsync(ldConsolePath, $"launch --index {index}", 8000);
        return exitCode == 0;
    }

    /// <summary>
    /// Closes an instance via 'ldconsole quit --index {index}'.
    /// </summary>
    public static async Task<bool> QuitInstanceAsync(string ldConsolePath, int index)
    {
        var (exitCode, _, _) = await RunCliCommandAsync(ldConsolePath, $"quit --index {index}", 8000);
        return exitCode == 0;
    }

    /// <summary>
    /// Monitors an instance until it reports 'running' or max wait time is exceeded.
    /// </summary>
    /// <summary>
    /// Checks if a specific LDPlayer instance is fully booted, its window is displayed,
    /// and ready for user/macro interaction.
    /// Does not treat a loading/starting splash screen or mere process presence as "running".
    /// </summary>
    public static async Task<bool> IsInstanceReadyAsync(string ldConsolePath, int index, int maxSubTimeoutMs = 2000)
    {
        if (string.IsNullOrEmpty(ldConsolePath) || !File.Exists(ldConsolePath))
            return false;

        // 1. Query instance details from 'ldconsole list2'
        var (instances, error) = await GetInstancesAsync(ldConsolePath);
        if (!string.IsNullOrEmpty(error) || instances == null)
            return false;

        var inst = instances.FirstOrDefault(i => i.Index == index);
        if (inst == null)
            return false;

        // In list2, if android_started (field 4) is false, Android has not started
        if (!inst.IsRunning)
            return false;

        // 2. Check Window availability and visibility
        bool hasTopWindow = inst.TopHwnd != IntPtr.Zero && IsWindow(inst.TopHwnd) && IsWindowVisible(inst.TopHwnd);
        bool hasBindWindow = inst.BindHwnd != IntPtr.Zero && IsWindow(inst.BindHwnd);

        // If TopHwnd is not valid/visible, check if PID main window is visible
        if (!hasTopWindow && inst.Pid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(inst.Pid);
                if (proc.MainWindowHandle != IntPtr.Zero && IsWindow(proc.MainWindowHandle) && IsWindowVisible(proc.MainWindowHandle))
                {
                    hasTopWindow = true;
                }
            }
            catch { }
        }

        // Window must be physically present and have reasonable dimensions (not 0x0 hidden)
        if (!hasTopWindow)
            return false;

        if (inst.TopHwnd != IntPtr.Zero)
        {
            GetWindowRect(inst.TopHwnd, out RECT topRect);
            int tw = topRect.Right - topRect.Left;
            int th = topRect.Bottom - topRect.Top;
            if (tw < 150 || th < 150)
                return false;
        }

        // If BindHwnd is present, verify it has rendered surface
        if (hasBindWindow)
        {
            GetWindowRect(inst.BindHwnd, out RECT bindRect);
            int bw = bindRect.Right - bindRect.Left;
            int bh = bindRect.Bottom - bindRect.Top;
            if (bw < 50 || bh < 50)
                return false;
        }

        // 3. Fast direct ADB check if adb.exe is available
        var tools = ResolveTools(Path.GetDirectoryName(ldConsolePath) ?? EmulatorManager.CurrentPath);
        int adbPort = 5555 + index * 2;
        int emuPort = 5554 + index * 2;
        int subBudget = Math.Clamp(maxSubTimeoutMs, 500, 2000);

        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            var (c1, out1, _) = await RunCliCommandAsync(tools.AdbPath, $"-s 127.0.0.1:{adbPort} shell getprop sys.boot_completed", subBudget);
            if (c1 == 0 && out1.Trim().Contains("1")) return true;

            var (c2, out2, _) = await RunCliCommandAsync(tools.AdbPath, $"-s emulator-{emuPort} shell getprop sys.boot_completed", subBudget);
            if (c2 == 0 && out2.Trim().Contains("1")) return true;
        }

        // 4. LDPlayer CLI device readiness check via 'ldconsole adb'
        int cliTimeout = Math.Clamp(maxSubTimeoutMs, 600, 2000);
        var (exitCode, bootOutput, _) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {index} --command \"shell getprop sys.boot_completed\"",
            cliTimeout);

        if (exitCode == 0 && bootOutput.Trim().Contains("1"))
        {
            return true;
        }

        // Check dev.bootcomplete
        var (exitDev, devBoot, _) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {index} --command \"shell getprop dev.bootcomplete\"",
            Math.Min(cliTimeout, 1200));

        if (exitDev == 0 && devBoot.Trim().Contains("1"))
        {
            return true;
        }

        // Check if boot animation stopped
        var (exitAnim, animOutput, _) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {index} --command \"shell getprop init.svc.bootanim\"",
            Math.Min(cliTimeout, 1200));

        if (exitAnim == 0 && animOutput.Trim().Equals("stopped", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Monitors an instance until it confirms full readiness (window visible and device ready),
    /// or until maxWaitSeconds is exceeded.
    /// Uses a monotonic Stopwatch clock and reports exact elapsed seconds continuously.
    /// </summary>
    public static async Task<(bool Ready, double ElapsedSeconds)> WaitUntilReadyDetailedAsync(
        string ldConsolePath,
        int index,
        int maxWaitSeconds,
        CancellationToken ct,
        Action<int> onElapsedSecond = null)
    {
        var stopwatch = Stopwatch.StartNew();
        double totalBudget = Math.Max(5, maxWaitSeconds);

        // Initial delay to give process time to spawn
        try { await Task.Delay(1000, ct); } catch { return (false, stopwatch.Elapsed.TotalSeconds); }

        int lastReportedSec = -1;

        while (stopwatch.Elapsed.TotalSeconds < totalBudget)
        {
            if (ct.IsCancellationRequested) return (false, stopwatch.Elapsed.TotalSeconds);

            var currentElapsed = (int)Math.Min(totalBudget, stopwatch.Elapsed.TotalSeconds);
            if (currentElapsed != lastReportedSec)
            {
                lastReportedSec = currentElapsed;
                onElapsedSecond?.Invoke(currentElapsed);
            }

            double remainingBudget = totalBudget - stopwatch.Elapsed.TotalSeconds;
            if (remainingBudget <= 0) break;

            int subTimeoutMs = Math.Clamp((int)(remainingBudget * 1000), 500, 2000);

            var ready = await IsInstanceReadyAsync(ldConsolePath, index, subTimeoutMs);
            if (ready)
            {
                stopwatch.Stop();
                return (true, stopwatch.Elapsed.TotalSeconds);
            }

            // Sleep in small 200ms intervals, updating the second ticker so the UI doesn't stall
            int delayMs = 1000;
            int slices = delayMs / 200;
            for (int s = 0; s < slices; s++)
            {
                if (stopwatch.Elapsed.TotalSeconds >= totalBudget || ct.IsCancellationRequested)
                    break;

                var tickSec = (int)Math.Min(totalBudget, stopwatch.Elapsed.TotalSeconds);
                if (tickSec != lastReportedSec)
                {
                    lastReportedSec = tickSec;
                    onElapsedSecond?.Invoke(tickSec);
                }

                try
                {
                    await Task.Delay(200, ct);
                }
                catch (OperationCanceledException)
                {
                    return (false, stopwatch.Elapsed.TotalSeconds);
                }
            }
        }

        stopwatch.Stop();
        var finalElapsed = Math.Min(totalBudget, stopwatch.Elapsed.TotalSeconds);
        onElapsedSecond?.Invoke((int)Math.Round(finalElapsed));
        return (false, stopwatch.Elapsed.TotalSeconds);
    }

    public static async Task<bool> WaitUntilReadyAsync(
        string ldConsolePath,
        int index,
        int maxWaitSeconds,
        CancellationToken ct,
        Action<int> onElapsedSecond = null)
    {
        var (ready, _) = await WaitUntilReadyDetailedAsync(ldConsolePath, index, maxWaitSeconds, ct, onElapsedSecond);
        return ready;
    }

    public static Task<bool> WaitUntilRunningAsync(
        string ldConsolePath,
        int index,
        int maxWaitSeconds,
        CancellationToken ct,
        Action<int> onElapsedSecond = null)
        => WaitUntilReadyAsync(ldConsolePath, index, maxWaitSeconds, ct, onElapsedSecond);

    /// <summary>
    /// Orchestrates sequential launching of selected instances with intervals, timeouts, and retries.
    /// Does not freeze the UI. Supports real-time progress callbacks and cancellation.
    /// </summary>
    public static async Task LaunchSequentiallyAsync(
        string ldConsolePath,
        IList<LdInstanceInfo> instances,
        LdLaunchOptions options,
        Action<LdInstanceInfo, string> onInstanceStatus,
        Action<string> onLogMessage,
        Action<int, int> onOverallProgress,
        CancellationToken ct)
    {
        if (instances == null || instances.Count == 0) return;

        int total = instances.Count;
        int current = 0;

        for (int i = 0; i < instances.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                onLogMessage?.Invoke("⏹ Đã dừng chuỗi khởi động theo yêu cầu của người dùng.");
                break;
            }

            var inst = instances[i];
            current++;
            onOverallProgress?.Invoke(current, total);

            // 1. Kiểm tra nếu tab đã chạy sẵn (tránh mở trùng)
            bool alreadyRunning = await IsInstanceReadyAsync(ldConsolePath, inst.Index);
            if (alreadyRunning)
            {
                inst.IsRunning = true;
                inst.OperationStatus = "Đang chạy";
                onInstanceStatus?.Invoke(inst, inst.OperationStatus);
                onLogMessage?.Invoke($"[Tab {inst.Index} - {inst.Name}]: Tab đã đang chạy và sẵn sàng, bỏ qua mở trùng.");
            }
            else
            {
                int maxRetries = Math.Max(0, options.MaxRetries);
                bool startedOk = false;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (ct.IsCancellationRequested) break;

                    string attemptText = attempt == 0 ? "Đang khởi động..." : $"Thử lại ({attempt}/{maxRetries})...";
                    inst.IsRunning = false;
                    inst.OperationStatus = attemptText;
                    onInstanceStatus?.Invoke(inst, inst.OperationStatus);
                    onLogMessage?.Invoke($"[Tab {inst.Index} - {inst.Name}]: {attemptText}");

                    // Gọi lệnh khởi động
                    await LaunchInstanceAsync(ldConsolePath, inst.Index);

                    // Chờ tab sẵn sàng với thời gian tối đa
                    (startedOk, double elapsedSec) = await WaitUntilReadyDetailedAsync(
                        ldConsolePath,
                        inst.Index,
                        options.MaxWaitSeconds,
                        ct,
                        elapsed =>
                        {
                            var status = $"Đang khởi động... ({elapsed}s/{options.MaxWaitSeconds}s)";
                            inst.OperationStatus = status;
                            onInstanceStatus?.Invoke(inst, status);
                        });

                    if (startedOk)
                    {
                        inst.IsRunning = true;
                        inst.OperationStatus = "Đang chạy";
                        onInstanceStatus?.Invoke(inst, inst.OperationStatus);
                        onLogMessage?.Invoke($"✔ [Tab #{inst.Index} - {inst.Name}]: Khởi động thành công, giả lập đã sẵn sàng (thời gian thực tế: {elapsedSec:F1}s / cấu hình: {options.MaxWaitSeconds}s).");
                        break;
                    }
                    else
                    {
                        if (ct.IsCancellationRequested) break;

                        inst.IsRunning = false;
                        inst.OperationStatus = "Khởi động thất bại hoặc quá thời gian";
                        onInstanceStatus?.Invoke(inst, inst.OperationStatus);
                        onLogMessage?.Invoke($"⚠ [Tab #{inst.Index} - {inst.Name}]: Quá thời gian chờ cấu hình {options.MaxWaitSeconds}s (thời gian thực tế đã chờ: {elapsedSec:F1}s). Tiến hành đóng tab...");
                        // Đóng tab chưa chạy được
                        await QuitInstanceAsync(ldConsolePath, inst.Index);
                        await Task.Delay(2000, ct);

                        if (attempt == maxRetries)
                        {
                            inst.IsRunning = false;
                            inst.OperationStatus = "Khởi động thất bại hoặc quá thời gian";
                            onInstanceStatus?.Invoke(inst, inst.OperationStatus);
                            onLogMessage?.Invoke($"✘ [Tab #{inst.Index} - {inst.Name}]: Khởi động thất bại sau {maxRetries + 1} lần thử.");
                        }
                    }
                }
            }

            // 2. Chờ khoảng cách giữa mỗi tab (nếu còn tab tiếp theo và chưa bị huỷ)
            if (i < instances.Count - 1 && !ct.IsCancellationRequested)
            {
                int interval = Math.Max(0, options.IntervalSeconds);
                if (interval > 0)
                {
                    for (int sec = interval; sec > 0; sec--)
                    {
                        if (ct.IsCancellationRequested) break;
                        onLogMessage?.Invoke($"⏳ Chờ {sec}s trước khi mở tab tiếp theo...");
                        await Task.Delay(1000, ct);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Closes the given list of instances asynchronously.
    /// </summary>
    public static async Task CloseInstancesAsync(
        string ldConsolePath,
        IList<LdInstanceInfo> instances,
        Action<LdInstanceInfo, string> onInstanceStatus,
        Action<string> onLogMessage,
        CancellationToken ct)
    {
        if (instances == null || instances.Count == 0) return;

        foreach (var inst in instances)
        {
            if (ct.IsCancellationRequested) break;
            inst.OperationStatus = "Đang đóng...";
            onInstanceStatus?.Invoke(inst, inst.OperationStatus);
            onLogMessage?.Invoke($"[Tab {inst.Index} - {inst.Name}]: Đang đóng...");

            await QuitInstanceAsync(ldConsolePath, inst.Index);
            inst.IsRunning = false;
            inst.OperationStatus = "Đã tắt";
            onInstanceStatus?.Invoke(inst, inst.OperationStatus);
            onLogMessage?.Invoke($"✔ [Tab {inst.Index} - {inst.Name}]: Đã đóng.");
        }
    }

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Captures the screenshot of a specific LDPlayer tab/instance.
    /// Captures the actual window bounds of the instance and saves to CapturesFolder.
    /// Returns a CaptureInfo with the saved path, offset, and dimensions.
    /// </summary>
    public static async Task<CaptureInfo> CaptureInstanceAsync(LdInstanceInfo instance, string baseFolder = null)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        var console = EmulatorManager.GetLdConsolePath() ?? ResolveTools(baseFolder ?? EmulatorManager.CurrentPath).LdConsolePath;
        if (string.IsNullOrEmpty(console) || !File.Exists(console))
        {
            throw new InvalidOperationException("Không tìm thấy tệp ldconsole.exe. Vui lòng kiểm tra lại cấu hình đường dẫn LDPlayer.");
        }

        bool isRunning = await IsRunningAsync(console, instance.Index);
        if (!isRunning)
        {
            throw new InvalidOperationException($"Tab #{instance.Index} ({instance.Name}) hiện đang tắt. Vui lòng khởi động tab trước.");
        }

        var tempFolder = ScreenshotService.CapturesFolder();
        if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
        var fileName = $"ldplayer_tab{instance.Index}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png";
        var localPath = Path.Combine(tempFolder, fileName);
        var remotePath = $"/sdcard/cap_{instance.Index}.png";

        var tools = ResolveTools(Path.GetDirectoryName(console));

        // 1. Primary: Direct ADB exec-out stream into localPath
        if (tools.HasAdb && File.Exists(tools.AdbPath))
        {
            try
            {
                int adbPort = 5555 + instance.Index * 2;
                string deviceSerial = $"127.0.0.1:{adbPort}";
                _ = await RunCliCommandAsync(tools.AdbPath, $"connect {deviceSerial}", 1500);

                var (capExit, pngBytes, _) = await RunCliCommandBytesAsync(tools.AdbPath, $"-s {deviceSerial} exec-out screencap -p", 4000);
                if (capExit == 0 && pngBytes != null && pngBytes.Length > 100 && IsPngHeader(pngBytes))
                {
                    await File.WriteAllBytesAsync(localPath, pngBytes);
                    using var bmp = new System.Drawing.Bitmap(localPath);
                    return new CaptureInfo(localPath, 0, 0, bmp.Width, bmp.Height);
                }
            }
            catch { }
        }

        // 2. Secondary: ldconsole adb shell screencap + pull
        var (cExit, _, cErr) = await RunCliCommandAsync(console,
            $"adb --index {instance.Index} --command \"shell screencap -p {remotePath}\"", 6000);

        if (cExit == 0)
        {
            var (pExit, _, pErr) = await RunCliCommandAsync(console,
                $"pull --index {instance.Index} --remote {remotePath} --local \"{localPath}\"", 6000);

            if (File.Exists(localPath) && new FileInfo(localPath).Length > 100)
            {
                using var bmp = new System.Drawing.Bitmap(localPath);
                return new CaptureInfo(localPath, 0, 0, bmp.Width, bmp.Height);
            }
        }

        throw new InvalidOperationException(
            $"Không thể chụp màn hình nền Tab #{instance.Index} ({instance.Name}): Kết nối ADB thất bại ({cErr}). Vui lòng đảm bảo đã bật 'Gỡ lỗi ADB' trong Cài đặt LDPlayer.");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, LdDeviceClient> _clients = new();
    public static LdDeviceClient GetClient(int tabIndex) =>
        _clients.GetOrAdd(tabIndex, idx => new LdDeviceClient(idx));

    /// <summary>
    /// Captures the Android screen of a specific LDPlayer tab directly from Android framebuffer into an OpenCV Mat.
    /// Does NOT capture desktop screen or PC windows. Coordinates are 100% Android internal device coordinates.
    /// </summary>
    public static TabCaptureMatResult CaptureTabMat(int tabIndex)
    {
        if (tabIndex < 0)
        {
            return new TabCaptureMatResult
            {
                Success = false,
                ErrorMessage = "Chưa xác định tab giả lập LDPlayer hợp lệ."
            };
        }

        return GetClient(tabIndex).CaptureFrame();
    }

    /// <summary>
    /// Sends background click/tap to a specific LDPlayer tab using Android internal coordinate system.
    /// Executes directly inside the Android instance via ADB.
    /// </summary>
    public static bool ClickTab(int tabIndex, int tabX, int tabY, string button = "left", int clicks = 1)
    {
        if (tabIndex < 0) return false;
        return GetClient(tabIndex).Tap(tabX, tabY, button, clicks);
    }

    public static bool DoubleTapTab(int tabIndex, int tabX, int tabY) =>
        ClickTab(tabIndex, tabX, tabY, "left", 2);

    /// <summary>
    /// Executes a background drag / swipe gesture on a specific LDPlayer tab.
    /// </summary>
    public static bool DragTab(int tabIndex, int x1, int y1, int x2, int y2, int durationMs = 300)
    {
        if (tabIndex < 0) return false;
        return GetClient(tabIndex).Swipe(x1, y1, x2, y2, durationMs);
    }

    /// <summary>
    /// Executes a background scroll gesture on a specific LDPlayer tab by swiping along the vertical axis.
    /// </summary>
    public static bool ScrollTab(int tabIndex, int x, int y, int amount)
    {
        if (tabIndex < 0 || amount == 0) return false;
        return GetClient(tabIndex).Scroll(x, y, amount);
    }

    /// <summary>
    /// Sends text into the active Android input field on the specified LDPlayer tab in the background.
    /// Returns detailed results including the chosen branch, execution status, and line count.
    /// Guarantees that the text is sent strictly once without duplicate or re-entrant calls.
    /// </summary>
    public static TypeTextResult InputTextDetailed(int tabIndex, string text, Action<string> log = null)
    {
        if (tabIndex < 0) return new TypeTextResult { Success = false, Branch = "none", Error = "Tab index không hợp lệ" };
        if (string.IsNullOrEmpty(text)) return new TypeTextResult { Success = true, Branch = "none (empty)", LinesCount = 0 };
        return GetClient(tabIndex).TypeTextDetailed(text, log);
    }

    /// <summary>
    /// Sends text into the active Android input field on the specified LDPlayer tab in the background.
    /// </summary>
    public static bool InputText(int tabIndex, string text)
    {
        return InputTextDetailed(tabIndex, text).Success;
    }

    public static string FormatAdbInputText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case ' ':
                    sb.Append("%s");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\'':
                    sb.Append("\\'");
                    break;
                case '$':
                    sb.Append("\\$");
                    break;
                case '&':
                    sb.Append("\\&");
                    break;
                case '|':
                    sb.Append("\\|");
                    break;
                case ';':
                    sb.Append("\\;");
                    break;
                case '<':
                    sb.Append("\\<");
                    break;
                case '>':
                    sb.Append("\\>");
                    break;
                case '(':
                    sb.Append("\\(");
                    break;
                case ')':
                    sb.Append("\\)");
                    break;
                case '`':
                    sb.Append("\\`");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sends a keypress to the specified LDPlayer tab in the background.
    /// </summary>
    public static bool PressKey(int tabIndex, string key, int repeat = 1, int intervalMs = 50)
    {
        if (tabIndex < 0) return false;
        return GetClient(tabIndex).PressKey(key, repeat, intervalMs);
    }

    /// <summary>
    /// Sends a hotkey combination to the specified LDPlayer tab in the background.
    /// </summary>
    public static bool PressHotkey(int tabIndex, string[] modifiers, string key)
    {
        if (tabIndex < 0) return false;
        return GetClient(tabIndex).PressHotkey(modifiers, key);
    }

    public static int MapKeyToAndroidKeyCode(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        var lower = key.Trim().ToLowerInvariant();

        return lower switch
        {
            "enter" or "return" => 66,
            "backspace" => 67,
            "escape" or "esc" or "back" => 4,
            "home" => 3,
            "tab" => 61,
            "space" => 62,
            "delete" or "del" => 112,
            "up" => 19,
            "down" => 20,
            "left" => 21,
            "right" => 22,
            "volume_up" or "volumeup" or "volup" => 24,
            "volume_down" or "volumedown" or "voldown" => 25,
            "menu" => 82,
            "app_switch" or "appswitch" => 187,
            "page_up" or "pageup" => 92,
            "page_down" or "pagedown" => 93,
            "search" => 84,
            "camera" => 27,
            _ => lower.Length == 1 && lower[0] >= 'a' && lower[0] <= 'z'
                ? 29 + (lower[0] - 'a')
                : (lower.Length == 1 && lower[0] >= '0' && lower[0] <= '9'
                    ? 7 + (lower[0] - '0')
                    : 0)
        };
    }

    /// <summary>
    /// Captures the specified LDPlayer tab directly to a PNG file on disk in the background.
    /// </summary>
    public static bool CaptureTabToDisk(int tabIndex, string localFilePath)
    {
        if (tabIndex < 0 || string.IsNullOrEmpty(localFilePath)) return false;
        var result = CaptureTabMat(tabIndex);
        if (!result.Success || result.Frame == null) return false;

        using (result.Frame)
        {
            return OpenCvSharp.Cv2.ImWrite(localFilePath, result.Frame);
        }
    }

    /// <summary>
    /// Resolves the target LDPlayer tab index for an action step.
    /// Checks the step parameters, execution context variables, and currently selected emulator instance.
    /// Returns -1 if the action is targeted at the general Windows PC desktop.
    /// </summary>
    public static int ResolveTabIndex(ExecutionContext ctx, ActionStep step)
    {
        if (step != null && step.Params.TryGetValue("tab_index", out var tiObj))
        {
            int tabIndex = ParamUtil.ToInt(tiObj, -1);
            if (tabIndex >= 0) return tabIndex;
        }

        if (ctx != null && ctx.Variables.TryGetValue("tab_index", out var viObj))
        {
            int tabIndex = ParamUtil.ToInt(viObj, -1);
            if (tabIndex >= 0) return tabIndex;
        }

        if (EmulatorManager.SelectedInstance != null && EmulatorManager.SelectedInstance.Index >= 0)
        {
            return EmulatorManager.SelectedInstance.Index;
        }

        return -1;
    }

    /// <summary>
    /// Checks and automatically configures 'basicSettings.adbDebug = 1' in the instance configuration file.
    /// </summary>
    public static bool EnsureAdbConfig(string baseFolder, int tabIndex)
    {
        try
        {
            var tools = ResolveTools(baseFolder ?? EmulatorManager.CurrentPath);
            if (!tools.IsValid) return false;

            var configPath = Path.Combine(tools.ToolDirectory, "vms", "config", $"leidian{tabIndex}.config");
            if (!File.Exists(configPath)) return false;

            var content = File.ReadAllText(configPath);
            if (content.Contains("\"basicSettings.adbDebug\": 0"))
            {
                content = content.Replace("\"basicSettings.adbDebug\": 0", "\"basicSettings.adbDebug\": 1");
                File.WriteAllText(configPath, content);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Retrieves the list of installed applications inside the specified LDPlayer tab.
    /// Queries user-installed packages first, followed by system packages.
    /// Provides detailed error messages and suggested remedies on failure.
    /// </summary>
    public static async Task<(List<LdAppInfo> Apps, string ErrorMessage, string Remedy)> GetInstalledPackagesAsync(
        string ldConsolePath, int tabIndex)
    {
        if (string.IsNullOrEmpty(ldConsolePath) || !File.Exists(ldConsolePath))
        {
            return (new List<LdAppInfo>(),
                "Chưa cấu hình đường dẫn tệp ldconsole.exe của LDPlayer.",
                "Vui lòng vào '⚙ Quản lý...' hoặc 'Cài đặt giả lập' để chọn đường dẫn cài đặt LDPlayer chính xác.");
        }

        if (tabIndex < 0)
        {
            return (new List<LdAppInfo>(),
                "Chưa chọn tab giả lập LDPlayer.",
                "Vui lòng tích chọn một tab giả lập trong danh sách bên trái trước khi lấy danh sách ứng dụng.");
        }

        var (instList, _) = await GetInstancesAsync(ldConsolePath);
        var targetInst = instList.FirstOrDefault(i => i.Index == tabIndex);

        bool isRunning = await IsRunningAsync(ldConsolePath, tabIndex);
        if (!isRunning)
        {
            string tabName = targetInst != null ? targetInst.Name : $"Tab #{tabIndex}";
            return (new List<LdAppInfo>(),
                $"{tabName} hiện đang tắt.",
                $"Vui lòng khởi động {tabName} (bấm nút '▶ Mở' trong danh sách) và đợi tab hiển thị trạng thái '🟢 Đang chạy' trước khi thử lại.");
        }

        // 1. Query third-party installed packages
        var (c1, outThirdParty, err1) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {tabIndex} --command \"pm list packages -3 -f\"",
            6000);

        // 2. Query all installed packages
        var (c2, outAll, err2) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {tabIndex} --command \"pm list packages -f\"",
            6000);

        if (string.IsNullOrWhiteSpace(outThirdParty) && string.IsNullOrWhiteSpace(outAll))
        {
            string detailErr = !string.IsNullOrWhiteSpace(err1) ? err1 : err2;
            if (!string.IsNullOrEmpty(detailErr) && (detailErr.Contains("device not found", StringComparison.OrdinalIgnoreCase) || detailErr.Contains("cannot connect", StringComparison.OrdinalIgnoreCase)))
            {
                return (new List<LdAppInfo>(),
                    $"Không thể kết nối dịch vụ ADB đến Tab #{tabIndex}.",
                    "Tab giả lập có thể chưa khởi động hoàn tất hệ điều hành Android hoặc ADB đang bị tắt. Hãy đợi tab hiển thị màn hình chính rồi thử lại.");
            }

            return (new List<LdAppInfo>(),
                $"Không lấy được danh sách ứng dụng từ Tab #{tabIndex}.",
                "Đảm bảo tab giả lập đang chạy và kết nối ADB hoạt động bình thường.");
        }

        var apps = ParsePackageList(outThirdParty, outAll);
        return (apps, null, null);
    }

    /// <summary>
    /// Parses package listing output from 'pm list packages -f' and 'pm list packages -3 -f'.
    /// Extracts package name, apk path, friendly display title, and third-party status.
    /// </summary>
    public static List<LdAppInfo> ParsePackageList(string thirdPartyRaw, string allRaw)
    {
        var result = new Dictionary<string, LdAppInfo>(StringComparer.OrdinalIgnoreCase);

        void ProcessRaw(string raw, bool isThirdParty)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("package:")) continue;
                var payload = trimmed.Substring(8);
                string apkPath = "";
                string pkgName = "";
                int eqIdx = payload.LastIndexOf('=');
                if (eqIdx >= 0)
                {
                    apkPath = payload.Substring(0, eqIdx).Trim();
                    pkgName = payload.Substring(eqIdx + 1).Trim();
                }
                else
                {
                    pkgName = payload.Trim();
                }

                if (string.IsNullOrWhiteSpace(pkgName)) continue;

                if (!result.TryGetValue(pkgName, out var existing))
                {
                    var friendlyName = GetFriendlyAppName(pkgName, apkPath);
                    result[pkgName] = new LdAppInfo
                    {
                        PackageName = pkgName,
                        DisplayName = friendlyName,
                        IsThirdParty = isThirdParty,
                        ApkPath = apkPath
                    };
                }
                else if (isThirdParty)
                {
                    existing.IsThirdParty = true;
                }
            }
        }

        ProcessRaw(thirdPartyRaw, true);
        ProcessRaw(allRaw, false);

        return result.Values
            .OrderByDescending(a => a.IsThirdParty)
            .ThenBy(a => a.DisplayName)
            .ToList();
    }

    private static readonly Dictionary<string, string> _knownAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com.google.android.youtube"] = "YouTube",
        ["com.android.chrome"] = "Google Chrome",
        ["com.facebook.katana"] = "Facebook",
        ["com.facebook.orca"] = "Messenger",
        ["com.facebook.lite"] = "Facebook Lite",
        ["com.zing.zalo"] = "Zalo",
        ["com.instagram.android"] = "Instagram",
        ["com.zhiliaoapp.musically"] = "TikTok",
        ["com.ss.android.ugc.trill"] = "TikTok (VN)",
        ["com.dts.freefireth"] = "Free Fire",
        ["com.dts.freefiremax"] = "Free Fire MAX",
        ["com.tencent.ig"] = "PUBG Mobile",
        ["com.vng.pubgmobile"] = "PUBG Mobile VN",
        ["com.miHoYo.GenshinImpact"] = "Genshin Impact",
        ["com.riotgames.league.wildrift"] = "Liên Minh: Tốc Chiến",
        ["com.garena.game.kgvn"] = "Liên Quân Mobile",
        ["com.google.android.apps.maps"] = "Google Maps",
        ["com.google.android.gm"] = "Gmail",
        ["com.google.android.play.games"] = "Google Play Games",
        ["com.android.vending"] = "Google Play Store",
        ["com.android.settings"] = "Cài đặt (Settings)",
        ["com.android.browser"] = "Trình duyệt (Browser)",
        ["com.android.camera2"] = "Máy ảnh (Camera)",
        ["com.android.gallery3d"] = "Bộ sưu tập (Gallery)",
        ["com.android.calculator2"] = "Máy tính (Calculator)",
        ["com.android.deskclock"] = "Đồng hồ (Clock)",
        ["com.android.contacts"] = "Danh bạ (Contacts)",
        ["com.android.music"] = "Âm nhạc (Music)",
        ["com.android.dialer"] = "Điện thoại (Phone)",
        ["com.android.mms"] = "Tin nhắn (Messages)",
        ["com.android.documentsui"] = "Tệp tin (Files)",
    };

    public static string GetFriendlyAppName(string packageName, string apkPath = "")
    {
        if (string.IsNullOrWhiteSpace(packageName)) return "";
        if (_knownAppNames.TryGetValue(packageName.Trim(), out var known))
            return known;

        // Try extracting from apk path (e.g. /data/app/~~.../AppName-1/base.apk)
        if (!string.IsNullOrWhiteSpace(apkPath))
        {
            var parts = apkPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var part = parts[i];
                if (part.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;
                if (part.StartsWith("~~")) continue;
                int dashIdx = part.IndexOf('-');
                var cleanPart = dashIdx > 0 ? part.Substring(0, dashIdx) : part;
                if (!cleanPart.Contains('.') && cleanPart.Length > 2)
                {
                    return CleanHumanReadableName(cleanPart);
                }
            }
        }

        // Fallback: use package name segments
        var pkgParts = packageName.Split('.');
        string lastPart = pkgParts[^1];
        if (pkgParts.Length > 2 && (lastPart.Equals("android", StringComparison.OrdinalIgnoreCase) || lastPart.Length <= 2))
        {
            lastPart = pkgParts[^2] + " " + lastPart;
        }

        return CleanHumanReadableName(lastPart);
    }

    private static string CleanHumanReadableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_' || c == '-')
            {
                sb.Append(' ');
            }
            else if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
                sb.Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }
        var formatted = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sb.ToString().Trim());
        return formatted;
    }

    /// <summary>
    /// Checks if a package is installed on the specified LDPlayer tab.
    /// </summary>
    public static async Task<bool> IsPackageInstalledAsync(string ldConsolePath, int tabIndex, string packageName)
    {
        if (string.IsNullOrWhiteSpace(ldConsolePath) || !File.Exists(ldConsolePath) || string.IsNullOrWhiteSpace(packageName))
            return false;

        var (exitCode, output, _) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {tabIndex} --command \"pm path {packageName.Trim()}\"",
            4000);

        return exitCode == 0 && output.Trim().StartsWith("package:");
    }

    /// <summary>
    /// Launches an Android application inside the specified LDPlayer tab via 'ldconsole runapp'.
    /// </summary>
    public static async Task<bool> LaunchAppAsync(string ldConsolePath, int tabIndex, string packageName)
    {
        if (string.IsNullOrWhiteSpace(ldConsolePath) || !File.Exists(ldConsolePath) || string.IsNullOrWhiteSpace(packageName))
            return false;

        var (exitCode, _, _) = await RunCliCommandAsync(
            ldConsolePath,
            $"runapp --index {tabIndex} --packagename {packageName.Trim()}",
            6000);

        if (exitCode == 0) return true;

        // Fallback: ADB monkey launcher
        var (mCode, _, _) = await RunCliCommandAsync(
            ldConsolePath,
            $"adb --index {tabIndex} --command \"monkey -p {packageName.Trim()} -c android.intent.category.LAUNCHER 1\"",
            5000);

        return mCode == 0;
    }
}

/// <summary>
/// Information about an application installed inside an LDPlayer tab.
/// </summary>
public class LdAppInfo
{
    public string PackageName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsThirdParty { get; set; } = true;
    public string ApkPath { get; set; } = "";

    public string DisplayText => string.IsNullOrEmpty(DisplayName) || DisplayName == PackageName
        ? PackageName
        : $"{DisplayName} ({PackageName})";

    public override string ToString() => DisplayText;
}

public struct TabCaptureMatResult
{
    public bool Success;
    public Mat Frame;
    public int DevW;
    public int DevH;
    public string ErrorMessage;
}



