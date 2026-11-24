using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Macro.Core.Screen;

/// <summary>
/// Screen capture + image search (OpenCV template matching) + pixel color +
/// screenshots. Coordinates are always global screen coordinates.
/// </summary>
public static class ImageFinder
{
    private static readonly object _lock = new();   // capture device guard

    public struct FoundImage
    {
        public int X, Y, W, H;         // position of the match
        public int Cx, Cy;             // center (handy for clicking)
        public double Score;
        public int TabIndex;           // -1 for global PC screen, >= 0 for LDPlayer tab
    }

    // ---------------------------------------------------------------- capture
    /// <summary>Capture a screen region into an OpenCV BGR Mat.</summary>
    public static Mat Capture(int x, int y, int width, int height)
    {
        var bounds = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
        using var bitmap = new Bitmap(bounds.Width, bounds.Height,
                                      PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
        }
        return BitmapToMat(bitmap);
    }

    /// <summary>Capture the whole virtual desktop (all monitors).</summary>
    public static (Mat mat, int offsetX, int offsetY) CaptureVirtualScreen()
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return (Capture(left, top, width, height), left, top);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>GDI+ 24bpp bitmaps already store pixels as BGR, so the copy
    /// is a straight scanline transfer.</summary>
    private static Mat BitmapToMat(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(System.Drawing.Point.Empty, bitmap.Size),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var mat = Mat.FromPixelData(bitmap.Height, bitmap.Width,
                                        MatType.CV_8UC3, data.Scan0,
                                        data.Stride);
            return mat.Clone();         // own the pixels before unlocking
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    // ------------------------------------------------------------ find image
    /// <summary>
    /// Search the screen for the template image until the timeout expires.
    /// Returns the best match (>= confidence) or null. Respects Stop/Pause.
    /// </summary>
    public static FoundImage? Find(ExecutionContext ctx, string imagePath,
                                   double confidence, int timeoutMs,
                                   (int x, int y, int w, int h)? region)
    {
        using var template = LoadTemplate(imagePath);
        using var grayTemplate = new Mat();
        Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGR2GRAY);
        int tw = template.Width, th = template.Height;

        if (region.HasValue && (region.Value.w < tw || region.Value.h < th))
            throw new ActionFailureException(
                "The search region is smaller than the template image.");

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(
            Math.Max(0, timeoutMs));
        while (true)
        {
            ctx.CheckStop();
            ctx.WaitIfPaused();

            int offsetX, offsetY;
            Mat frame;
            if (region.HasValue)
            {
                var r = region.Value;
                frame = Capture(r.x, r.y, r.w, r.h);
                offsetX = r.x;
                offsetY = r.y;
            }
            else
            {
                var (mat, ox, oy) = CaptureVirtualScreen();
                frame = mat;
                offsetX = ox;
                offsetY = oy;
            }

            using (frame)
            {
                if (frame.Height >= th && frame.Width >= tw)
                {
                    using var grayFrame = new Mat();
                    Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
                    using var result = new Mat();
                    Cv2.MatchTemplate(grayFrame, grayTemplate, result,
                                      TemplateMatchModes.CCoeffNormed);
                    Cv2.MinMaxLoc(result, out _, out var maxVal,
                                  out _, out var maxLoc);
                    if (maxVal >= confidence)
                    {
                        return new FoundImage
                        {
                            X = maxLoc.X + offsetX,
                            Y = maxLoc.Y + offsetY,
                            W = tw,
                            H = th,
                            Cx = maxLoc.X + offsetX + tw / 2,
                            Cy = maxLoc.Y + offsetY + th / 2,
                            Score = Math.Round(maxVal, 3),
                            TabIndex = -1,
                        };
                    }
                }
            }

            if (DateTime.UtcNow >= deadline) return null;
            ctx.Sleep(0.12);
        }
    }

    /// <summary>
    /// Searches for the template image strictly inside the screen of the specified LDPlayer tab.
    /// Does not capture PC desktop or other windows.
    /// Returns FoundImage with coordinates mapped to the emulator's internal device coordinate system (0..devW-1, 0..devH-1).
    /// If tab is not running or cannot be captured, throws ActionFailureException without fallback to PC screen.
    /// </summary>
    public static FoundImage? FindInTab(
        ExecutionContext ctx,
        int tabIndex,
        string imagePath,
        double confidence,
        int timeoutMs,
        bool useRegion = false,
        int rx = 0, int ry = 0, int rw = 0, int rh = 0)
    {
        if (tabIndex < 0)
            throw new ActionFailureException(
                "Chưa xác định được tab giả lập LDPlayer cho thao tác tìm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro.");

        using var template = LoadTemplate(imagePath);
        using var grayTemplate = new Mat();
        Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGR2GRAY);
        int tw = template.Width, th = template.Height;

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));

        while (true)
        {
            ctx.CheckStop();
            ctx.WaitIfPaused();

            var capResult = Macro.Core.Emulator.LdPlayerService.CaptureTabMat(tabIndex);
            if (!capResult.Success || capResult.Frame == null)
            {
                throw new ActionFailureException(
                    $"Không thể chụp màn hình bên trong tab giả lập #{tabIndex}: {capResult.ErrorMessage ?? "Tab không sẵn sàng hoặc không thể truy cập."}");
            }

            using var frame = capResult.Frame;
            int winW = frame.Width;
            int winH = frame.Height;
            int devW = capResult.DevW;
            int devH = capResult.DevH;

            OpenCvSharp.Rect searchRect;
            if (useRegion && rw > 0 && rh > 0)
            {
                var (bx, by, cw, ch) = Macro.Core.Emulator.LdPlayerService.CalculateContentBounds(winW, winH, devW, devH);
                double scaleX = (double)cw / devW;
                double scaleY = (double)ch / devH;
                int frameRx = bx + (int)Math.Round(rx * scaleX);
                int frameRy = by + (int)Math.Round(ry * scaleY);
                int frameRw = (int)Math.Round(rw * scaleX);
                int frameRh = (int)Math.Round(rh * scaleY);

                frameRx = Math.Clamp(frameRx, 0, winW - 1);
                frameRy = Math.Clamp(frameRy, 0, winH - 1);
                frameRw = Math.Clamp(frameRw, 1, winW - frameRx);
                frameRh = Math.Clamp(frameRh, 1, winH - frameRy);
                searchRect = new OpenCvSharp.Rect(frameRx, frameRy, frameRw, frameRh);
            }
            else
            {
                searchRect = new OpenCvSharp.Rect(0, 0, winW, winH);
            }

            if (searchRect.Width >= tw && searchRect.Height >= th)
            {
                using var subFrame = new Mat(frame, searchRect);
                using var graySub = new Mat();
                Cv2.CvtColor(subFrame, graySub, ColorConversionCodes.BGR2GRAY);
                using var matchResult = new Mat();
                Cv2.MatchTemplate(graySub, grayTemplate, matchResult, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(matchResult, out _, out var maxVal, out _, out var maxLoc);

                if (maxVal >= confidence)
                {
                    int matchFrameX = searchRect.X + maxLoc.X;
                    int matchFrameY = searchRect.Y + maxLoc.Y;

                    var (tabX1, tabY1) = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(
                        winW, winH, devW, devH, matchFrameX, matchFrameY);
                    var (tabX2, tabY2) = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(
                        winW, winH, devW, devH, matchFrameX + tw, matchFrameY + th);
                    var (tabCx, tabCy) = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(
                        winW, winH, devW, devH, matchFrameX + tw / 2.0, matchFrameY + th / 2.0);

                    return new FoundImage
                    {
                        X = tabX1,
                        Y = tabY1,
                        W = Math.Max(1, tabX2 - tabX1),
                        H = Math.Max(1, tabY2 - tabY1),
                        Cx = tabCx,
                        Cy = tabCy,
                        Score = Math.Round(maxVal, 3),
                        TabIndex = tabIndex,
                    };
                }
            }

            if (DateTime.UtcNow >= deadline) return null;
            ctx.Sleep(0.12);
        }
    }

    /// <summary>Read an image file (unicode-safe: read bytes, then decode).</summary>
    public static Mat LoadTemplate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ActionFailureException(
                "No image file is set - browse to a PNG/JPG first");
        var resolved = Macro.Core.Services.ScreenshotService.ResolveImagePath(path);
        if (!File.Exists(resolved))
            throw new ActionFailureException($"Image file not found: {path}");
        var bytes = File.ReadAllBytes(resolved);
        var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat == null || mat.Empty())
            throw new ActionFailureException($"Cannot read image file: {path}");
        return mat;
    }

    // ---------------------------------------------------------------- pixels
    public static string GetPixelColor(int x, int y)
    {
        using var mat = Capture(x, y, 1, 1);
        var indexer = mat.Get<Vec3b>(0, 0);   // BGR
        return $"#{indexer.Item2:X2}{indexer.Item1:X2}{indexer.Item0:X2}";
    }

    public static string SaveScreenshot(string prefix)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "macro", "screenshots");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder,
            $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var (mat, _, _) = CaptureVirtualScreen();
        using (mat)
        {
            Cv2.ImWrite(path, mat);
        }
        return path;
    }
}
