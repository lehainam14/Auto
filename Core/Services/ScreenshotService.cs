using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Macro.Core.Services;

/// <summary>Info about one connected monitor.</summary>
public record MonitorInfo(int Index, string Name, int X, int Y, int Width, int Height)
{
    public override string ToString() => Name;
}

/// <summary>Metadata of the most recent capture (path + screen offset +
/// original size) used for image-to-screen coordinate mapping.</summary>
public record CaptureInfo(string Path, int OffsetX, int OffsetY, int Width, int Height);

/// <summary>
/// Screenshot Service. Designed to grow new sources later (window,
/// Android emulator, device, file) - today it implements the desktop:
/// the whole virtual screen or one specific monitor. Captures are saved
/// as PNG under Documents\macro\captures and remembered with their screen
/// offset so image coordinates can be mapped back to screen coordinates.
/// </summary>
public static class ScreenshotService
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private static readonly List<CaptureInfo> _captures = new();

    /// <summary>All captures taken this session (newest last).</summary>
    public static IReadOnlyList<CaptureInfo> Captures => _captures;

    public static CaptureInfo LastCapture => _captures.Count > 0
        ? _captures[^1] : null;

    public static string CapturesFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "macro", "captures");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string TemplatesFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "macro", "templates");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Resolves an image path by checking the exact path, relative to macro file,
    /// in the standard templates folder, or in the current working directory.
    /// </summary>
    public static string ResolveImagePath(string path, string macroFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        // 1. Direct path check (e.g. absolute path or already existing relative path)
        if (File.Exists(path))
            return Path.GetFullPath(path);

        // 2. Relative to macro file location if provided
        if (!string.IsNullOrEmpty(macroFilePath))
        {
            var macroDir = Path.GetDirectoryName(macroFilePath);
            if (!string.IsNullOrEmpty(macroDir))
            {
                var candidate = Path.Combine(macroDir, path);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);

                var inMacroTemplates = Path.Combine(macroDir, "templates", Path.GetFileName(path));
                if (File.Exists(inMacroTemplates)) return Path.GetFullPath(inMacroTemplates);
            }
        }

        // 3. Check in standard persistent templates folder
        try
        {
            var templatesFolder = TemplatesFolder();
            var fileNameOnly = Path.GetFileName(path);
            var inTemplates = Path.Combine(templatesFolder, fileNameOnly);
            if (File.Exists(inTemplates)) return Path.GetFullPath(inTemplates);
        }
        catch { }

        // 4. Check in current working directory
        try
        {
            var candidateCwd = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (File.Exists(candidateCwd)) return Path.GetFullPath(candidateCwd);
        }
        catch { }

        return path;
    }

    // ---------------------------------------------------------------- monitors
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip,
        MonitorEnumProc callback, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT rect, IntPtr data);

    /// <summary>List every connected monitor (index starts at 1).</summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    monitors.Add(new MonitorInfo(
                        monitors.Count + 1,
                        $"Monitor {monitors.Count + 1} ({info.szDevice})",
                        info.rcMonitor.Left, info.rcMonitor.Top,
                        info.rcMonitor.Right - info.rcMonitor.Left,
                        info.rcMonitor.Bottom - info.rcMonitor.Top));
                }
                return true;
            }, IntPtr.Zero);
        return monitors;
    }

    // ---------------------------------------------------------------- capture
    /// <summary>Capture a rectangular area into a PNG file and remember it.</summary>
    public static CaptureInfo Capture(int x, int y, int width, int height,
                                      string name = null)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Invalid capture size");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        }
        var fileName = (string.IsNullOrWhiteSpace(name) ? "capture" : name)
            + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(CapturesFolder(), fileName);
        bitmap.Save(path, ImageFormat.Png);
        var info = new CaptureInfo(path, x, y, width, height);
        _captures.Add(info);
        return info;
    }

    /// <summary>Capture the whole virtual desktop (all monitors together).</summary>
    public static CaptureInfo CaptureVirtualScreen(string name = null)
    {
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return Capture(left, top, width, height, name);
    }

    /// <summary>Capture one monitor by index (1-based).</summary>
    public static CaptureInfo CaptureMonitor(int monitorIndex, string name = null)
    {
        var monitors = GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Index == monitorIndex)
                      ?? monitors.FirstOrDefault()
                      ?? throw new InvalidOperationException("No monitor found");
        return Capture(monitor.X, monitor.Y, monitor.Width, monitor.Height, name);
    }

    public static string SaveTemplate(System.Drawing.Bitmap bitmap, string name)
    {
        var fileName = (string.IsNullOrWhiteSpace(name) ? "template" : name)
            + "_" + DateTime.Now.ToString("HHmmss") + ".png";
        fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(TemplatesFolder(), fileName);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}

/// <summary>
/// Coordinate Mapping Service: converts between image pixel coordinates
/// (of a capture) and global screen coordinates, taking the capture's
/// screen offset into account. Zoom/pan never leaks into these values -
/// the Image Viewer maps its display coordinates to original-image pixels
/// first, then this service maps image pixels to the screen.
/// </summary>
public static class CoordinateMapping
{
    /// <summary>Image pixel -> global screen coordinate.</summary>
    public static (int screenX, int screenY) ImageToScreen(
        CaptureInfo capture, int imageX, int imageY)
        => (capture.OffsetX + imageX, capture.OffsetY + imageY);

    /// <summary>Global screen coordinate -> image pixel (may be outside bounds).</summary>
    public static (int imageX, int imageY) ScreenToImage(
        CaptureInfo capture, int screenX, int screenY)
        => (screenX - capture.OffsetX, screenY - capture.OffsetY);

    /// <summary>Clamp a point into the capture bounds.</summary>
    public static (int x, int y) ClampToImage(CaptureInfo capture, int x, int y)
        => (Math.Clamp(x, 0, capture.Width - 1),
            Math.Clamp(y, 0, capture.Height - 1));
}
