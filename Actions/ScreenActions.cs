using System.IO;
using Macro.Core;
using Macro.Core.Input;
using Macro.Core.Screen;
using Macro.Core.Services;
using Macro.Localization;
using ExecutionContext = Macro.Core.ExecutionContext;

namespace Macro.Actions;

/// <summary>Image / screen actions: find image, click image, wait for
/// image, image exists, screenshot, pixel color.</summary>
public static class ScreenActions
{
    private static ParamSpec ImageSpec => new(
        "image", Loc.T("param.image", "Template image"), ParamWidget.ImageFile)
    {
        Tooltip = "A small PNG/JPG crop of what you want to find on screen. "
                + "Use 'Capture & crop' to cut a region from a screenshot.",
    };

    private static ParamSpec ConfidenceSpec => new(
        "confidence", Loc.T("param.confidence", "Confidence"), ParamWidget.Slider)
    {
        Min = 0.5, Max = 1.0,
        Tooltip = "How closely the screen must match the image. "
                + "0.9 = strict, 0.7 = loose.",
    };

    private static ParamSpec TimeoutSpec => new(
        "timeout_ms", Loc.T("param.timeout_ms", "Timeout"), ParamWidget.Int)
    {
        Min = 100, Max = 600000, Unit = "ms",
        Tooltip = "How long to keep searching before giving up.",
    };

    private static ParamSpec RegionSpec => new(
        "region", Loc.T("param.region", "Search region"), ParamWidget.Region)
    {
        Tooltip = "Limit the search to part of the screen - much faster.",
    };

    private static ParamSpec OnFoundSpec => new(
        "on_found", Loc.T("param.on_found", "Nếu tìm thấy ảnh"), ParamWidget.Enum)
    {
        Options = new List<(string, object)>
        {
            ("opt.on_found.continue", "continue"),
            ("opt.on_found.click", "click"),
            ("opt.on_found.goto", "goto"),
            ("opt.on_found.click_and_goto", "click_and_goto"),
        },
        Tooltip = "Hành động khi tìm thấy ảnh: tiếp tục, click vào ảnh, hoặc chuyển đến lệnh khác.",
    };

    private static ParamSpec FoundTargetSpec => new(
        "found_target_id", Loc.T("param.found_target", "Lệnh đích khi tìm thấy"),
        ParamWidget.StepSelector)
    {
        Tooltip = "Chọn lệnh sẽ chuyển đến khi tìm thấy ảnh.",
    };

    private static ParamSpec NotFoundSpec => new(
        "not_found", Loc.T("param.not_found", "If image is not found"),
        ParamWidget.Enum)
    {
        Options = new List<(string, object)>
        {
            ("opt.fail", "fail"),
            ("opt.continue", "continue"),
            ("opt.goto", "goto"),
        },
    };

    private static ParamSpec NotFoundTargetSpec => new(
        "not_found_target_id", Loc.T("param.not_found_target", "Lệnh đích khi không thấy"),
        ParamWidget.StepSelector)
    {
        Tooltip = "Chọn lệnh sẽ chuyển đến khi không tìm thấy ảnh.",
    };

    private static string NotFoundLabel(object value) =>
        Loc.T("opt." + ParamUtil.ToStringValue(value, "fail"),
              ParamUtil.ToStringValue(value, "fail"));

    private static string TabSuffix(IDictionary<string, object> p) =>
        p.TryGetValue("tab_index", out var ti) && ParamUtil.ToInt(ti, -1) >= 0
            ? $" [Tab #{ParamUtil.ToInt(ti)}]"
            : "";

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

        if (Macro.Core.Emulator.EmulatorManager.SelectedInstance != null &&
            Macro.Core.Emulator.EmulatorManager.SelectedInstance.Index >= 0)
        {
            return Macro.Core.Emulator.EmulatorManager.SelectedInstance.Index;
        }

        return -1;
    }

    private static (int x, int y, int w, int h)? RegionOf(ActionStep step)
    {
        if (step.GetBool("use_region"))
        {
            var w = step.GetInt("region_w");
            var h = step.GetInt("region_h");
            if (w > 0 && h > 0)
                return (step.GetInt("region_x"), step.GetInt("region_y"), w, h);
        }
        if (step.Params.TryGetValue("tab_index", out var tiObj))
        {
            int tabIndex = ParamUtil.ToInt(tiObj, -1);
            if (tabIndex >= 0)
            {
                var tabBounds = Macro.Core.Emulator.LdPlayerService.GetTabWindowBounds(tabIndex);
                if (tabBounds.HasValue)
                    return tabBounds.Value;
            }
        }
        return null;
    }

    private static string ShortName(string path) =>
        string.IsNullOrEmpty(path)
            ? Loc.T("sum.noimage", "(no image)")
            : Path.GetFileName(path);

    private static string ValidateImage(ActionStep step)
    {
        var rawPath = step.GetStr("image");
        if (string.IsNullOrWhiteSpace(rawPath))
            return Loc.T("msg.no.image.set",
                "No image file is set - browse to a PNG/JPG first");

        var resolved = Macro.Core.Services.ScreenshotService.ResolveImagePath(rawPath);
        if (!File.Exists(resolved))
            return Loc.F("msg.image.file.missing", "Image file not found: {0}", rawPath);

        return null;
    }

    public static void Register()
    {
        RegisterFindImage();
        RegisterClickImage();
        RegisterWaitForImage();
        RegisterImageExists();
        RegisterScreenshot();
        RegisterPixelColor();
    }

    // ------------------------------------------------------------ find image
    private static void RegisterFindImage()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "find_image",
            Name = "Find Image",
            Category = "screen",
            Icon = "image",
            Description = "Search the screen for an image and remember its "
                        + "position (also acts as 'Wait For Image').",
            ParamSchema = { ImageSpec, ConfidenceSpec, TimeoutSpec,
                            RegionSpec, OnFoundSpec, FoundTargetSpec,
                            NotFoundSpec, NotFoundTargetSpec },
            DefaultParams =
            {
                ["image"] = "", ["confidence"] = 0.9, ["timeout_ms"] = 5000,
                ["on_found"] = "continue", ["found_target_id"] = "",
                ["not_found"] = "fail", ["not_found_target_id"] = "",
                ["use_region"] = false, ["region_x"] = 0, ["region_y"] = 0,
                ["region_w"] = 0, ["region_h"] = 0,
            },
            Summary = p =>
            {
                var baseText = Loc.F("sum.find_image",
                    "{0}  |  timeout {1} ms",
                    ShortName(ParamUtil.ToStringValue(p.GetValueOrDefault("image"))),
                    ParamUtil.ToInt(p.GetValueOrDefault("timeout_ms"), 5000));
                var onFound = ParamUtil.ToStringValue(p.GetValueOrDefault("on_found"), "continue");
                var notFound = ParamUtil.ToStringValue(p.GetValueOrDefault("not_found"), "fail");
                var branchParts = new List<string>();
                if (onFound == "click") branchParts.Add(Loc.T("sum.found.click", "Click nếu thấy"));
                else if (onFound == "goto") branchParts.Add(Loc.T("sum.found.goto", "Chuyển lệnh nếu thấy"));
                else if (onFound == "click_and_goto") branchParts.Add(Loc.T("sum.found.click_goto", "Click & chuyển lệnh"));

                if (notFound == "continue") branchParts.Add(Loc.T("sum.notfound.continue", "Bỏ qua nếu không thấy"));
                else if (notFound == "goto") branchParts.Add(Loc.T("sum.notfound.goto", "Chuyển lệnh nếu không thấy"));

                if (branchParts.Count > 0)
                    baseText += $" ({string.Join(", ", branchParts)})";

                return baseText + TabSuffix(p);
            },
            Validate = step =>
            {
                var imgErr = ValidateImage(step);
                if (imgErr != null) return imgErr;

                var onFound = step.GetStr("on_found", "continue");
                if (onFound is "goto" or "click_and_goto")
                {
                    var target = step.GetStr("found_target_id");
                    if (string.IsNullOrWhiteSpace(target))
                        return Loc.T("val.find_image.no_found_target", "Chưa chọn lệnh đích khi tìm thấy ảnh.");
                    if (target == step.Id)
                        return Loc.T("val.find_image.self_found_target", "Không thể chuyển đến chính dòng lệnh tìm ảnh.");
                }

                var notFound = step.GetStr("not_found", "fail");
                if (notFound == "goto")
                {
                    var target = step.GetStr("not_found_target_id");
                    if (string.IsNullOrWhiteSpace(target))
                        return Loc.T("val.find_image.no_notfound_target", "Chưa chọn lệnh đích khi không tìm thấy ảnh.");
                    if (target == step.Id)
                        return Loc.T("val.find_image.self_notfound_target", "Không thể chuyển đến chính dòng lệnh tìm ảnh.");
                }

                return null;
            },
            Execute = (ctx, step) =>
            {
                int tabIndex = ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.find_image.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác tìm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                bool useRegion = step.GetBool("use_region");
                var found = ImageFinder.FindInTab(
                    ctx,
                    tabIndex,
                    step.GetStr("image"),
                    step.GetDouble("confidence", 0.9),
                    step.GetInt("timeout_ms", 5000),
                    useRegion,
                    step.GetInt("region_x"),
                    step.GetInt("region_y"),
                    step.GetInt("region_w"),
                    step.GetInt("region_h"));

                if (found.HasValue)
                {
                    ctx.Variables["last_found_image"] = found.Value;
                    ctx.Variables["image_exists"] = true;
                    ctx.Log(Loc.F("msg.image.found.tab",
                        "Tìm thấy ảnh trên Tab #{0} tại tọa độ giả lập ({1}, {2}), độ khớp {3}",
                        tabIndex, found.Value.Cx, found.Value.Cy, found.Value.Score));

                    var onFound = step.GetStr("on_found", "continue");
                    if (onFound is "click" or "click_and_goto")
                    {
                        var clickX = found.Value.Cx;
                        var clickY = found.Value.Cy;
                        ctx.Log(Loc.F("msg.click.image.tab",
                            "Click vào ảnh tại tọa độ giả lập ({0}, {1}) trên Tab #{2}",
                            clickX, clickY, tabIndex));
                        Macro.Core.Emulator.LdPlayerService.ClickTab(tabIndex, clickX, clickY, "left");
                    }

                    if (onFound is "goto" or "click_and_goto")
                    {
                        var targetId = step.GetStr("found_target_id");
                        if (string.IsNullOrWhiteSpace(targetId))
                            throw new ActionFailureException(Loc.T("val.find_image.no_found_target", "Chưa chọn lệnh đích khi tìm thấy ảnh."));
                        if (targetId == step.Id)
                            throw new ActionFailureException(Loc.T("val.find_image.self_found_target", "Không thể chuyển đến chính dòng lệnh tìm ảnh."));
                        ctx.Log(Loc.F("msg.goto.executing", "Chuyển luồng thực thi đến lệnh {0}", targetId));
                        throw new GotoStepException(targetId);
                    }
                    return;
                }

                ctx.Variables["last_found_image"] = null;
                ctx.Variables["image_exists"] = false;

                var notFoundMode = step.GetStr("not_found", "fail");
                if (notFoundMode == "goto")
                {
                    var targetId = step.GetStr("not_found_target_id");
                    if (string.IsNullOrWhiteSpace(targetId))
                        throw new ActionFailureException(Loc.T("val.find_image.no_notfound_target", "Chưa chọn lệnh đích khi không tìm thấy ảnh."));
                    if (targetId == step.Id)
                        throw new ActionFailureException(Loc.T("val.find_image.self_notfound_target", "Không thể chuyển đến chính dòng lệnh tìm ảnh."));
                    ctx.Log(Loc.F("msg.goto.executing", "Chuyển luồng thực thi đến lệnh {0}", targetId));
                    throw new GotoStepException(targetId);
                }

                if (notFoundMode == "continue")
                {
                    ctx.Log(Loc.T("msg.image.notfound.continue",
                        "Image not found - continuing"));
                    return;
                }

                throw new ActionFailureException(Loc.F(
                    "msg.image.notfound.tab",
                    "Không tìm thấy ảnh trên Tab #{0} trong vòng {1} ms: {2}",
                    tabIndex, step.GetInt("timeout_ms", 5000), step.GetStr("image")));
            },
        });
    }

    // ------------------------------------------------------------ click image
    private static void RegisterClickImage()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "click_image",
            Name = "Click Image",
            Category = "screen",
            Icon = "clickimage",
            Description = "Find an image on screen and click it (center by "
                        + "default, or a relative point inside the template).",
            HiddenFromLibrary = true,
            ParamSchema =
            {
                ImageSpec, ConfidenceSpec, TimeoutSpec, RegionSpec,
                new("button", Loc.T("param.button", "Mouse button"), ParamWidget.Enum)
                {
                    Options = new List<(string, object)>
                    {
                        ("opt.left", "left"), ("opt.middle", "middle"),
                        ("opt.right", "right"),
                    },
                },
                new("point_x", Loc.T("param.point_x", "X within template (0-1)"),
                    ParamWidget.Double)
                { Min = 0, Max = 1, Tooltip = "0 = left edge, 0.5 = center, 1 = right edge." },
                new("point_y", Loc.T("param.point_y", "Y within template (0-1)"),
                    ParamWidget.Double)
                { Min = 0, Max = 1, Tooltip = "0 = top edge, 0.5 = center, 1 = bottom edge." },
                NotFoundSpec,
            },
            DefaultParams =
            {
                ["image"] = "", ["confidence"] = 0.9, ["timeout_ms"] = 5000,
                ["button"] = "left", ["point_x"] = 0.5, ["point_y"] = 0.5,
                ["not_found"] = "fail", ["use_region"] = false,
                ["region_x"] = 0, ["region_y"] = 0, ["region_w"] = 0,
                ["region_h"] = 0,
            },
            Summary = p => Loc.F("sum.click_image", "{0}  |  {1} click",
                ShortName(ParamUtil.ToStringValue(p.GetValueOrDefault("image"))),
                ParamUtil.ToStringValue(p.GetValueOrDefault("button"), "left")) + TabSuffix(p),
            Validate = ValidateImage,
            Execute = (ctx, step) =>
            {
                int tabIndex = ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.find_image.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác tìm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                bool useRegion = step.GetBool("use_region");
                var found = ImageFinder.FindInTab(
                    ctx,
                    tabIndex,
                    step.GetStr("image"),
                    step.GetDouble("confidence", 0.9),
                    step.GetInt("timeout_ms", 5000),
                    useRegion,
                    step.GetInt("region_x"),
                    step.GetInt("region_y"),
                    step.GetInt("region_w"),
                    step.GetInt("region_h"));

                if (!found.HasValue)
                {
                    ctx.Variables["last_found_image"] = null;
                    ctx.Variables["image_exists"] = false;
                    if (step.GetStr("not_found", "fail") == "continue")
                    {
                        ctx.Log(Loc.T("msg.skip.click",
                            "Image not found - skipping click"));
                        return;
                    }
                    throw new ActionFailureException(Loc.F(
                        "msg.image.notfound.tab",
                        "Không tìm thấy ảnh trên Tab #{0} trong vòng {1} ms: {2}",
                        tabIndex, step.GetInt("timeout_ms", 5000), step.GetStr("image")));
                }
                ctx.Variables["last_found_image"] = found.Value;
                ctx.Variables["image_exists"] = true;
                // Click at a relative point inside the found rectangle in emulator coords
                var fx = Math.Clamp(step.GetDouble("point_x", 0.5), 0, 1);
                var fy = Math.Clamp(step.GetDouble("point_y", 0.5), 0, 1);
                var clickX = found.Value.X + (int)Math.Round(found.Value.W * fx);
                var clickY = found.Value.Y + (int)Math.Round(found.Value.H * fy);
                ctx.Log(Loc.F("msg.click.image.tab",
                    "Click vào ảnh tại tọa độ giả lập ({0}, {1}) trên Tab #{2}",
                    clickX, clickY, tabIndex));
                Macro.Core.Emulator.LdPlayerService.ClickTab(tabIndex, clickX, clickY, step.GetStr("button", "left"));
            },
        });
    }

    // -------------------------------------------------------- wait for image
    private static void RegisterWaitForImage()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "wait_for_image",
            Name = "Wait For Image",
            Category = "screen",
            Icon = "wait",
            Description = "Wait until the template appears on screen (or the "
                        + "timeout expires).",
            ParamSchema =
            {
                ImageSpec, ConfidenceSpec, TimeoutSpec, RegionSpec, NotFoundSpec,
            },
            DefaultParams =
            {
                ["image"] = "", ["confidence"] = 0.9, ["timeout_ms"] = 30000,
                ["not_found"] = "fail", ["use_region"] = false,
                ["region_x"] = 0, ["region_y"] = 0, ["region_w"] = 0,
                ["region_h"] = 0,
            },
            Summary = p => Loc.F("sum.find_image",
                "{0}  |  timeout {1} ms",
                ShortName(ParamUtil.ToStringValue(p.GetValueOrDefault("image"))),
                ParamUtil.ToInt(p.GetValueOrDefault("timeout_ms"), 5000)) + TabSuffix(p),
            Validate = ValidateImage,
            Execute = (ctx, step) =>
            {
                int tabIndex = ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.find_image.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác tìm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                bool useRegion = step.GetBool("use_region");
                var found = ImageFinder.FindInTab(
                    ctx,
                    tabIndex,
                    step.GetStr("image"),
                    step.GetDouble("confidence", 0.9),
                    step.GetInt("timeout_ms", 30000),
                    useRegion,
                    step.GetInt("region_x"),
                    step.GetInt("region_y"),
                    step.GetInt("region_w"),
                    step.GetInt("region_h"));

                if (found.HasValue)
                {
                    ctx.Variables["last_found_image"] = found.Value;
                    ctx.Variables["image_exists"] = true;
                    ctx.Log(Loc.F("msg.image.found.tab",
                        "Tìm thấy ảnh trên Tab #{0} tại tọa độ giả lập ({1}, {2}), độ khớp {3}",
                        tabIndex, found.Value.Cx, found.Value.Cy, found.Value.Score));
                    return;
                }
                ctx.Variables["last_found_image"] = null;
                ctx.Variables["image_exists"] = false;
                if (step.GetStr("not_found", "fail") == "fail")
                    throw new ActionFailureException(Loc.F(
                        "msg.image.notfound.tab",
                        "Không tìm thấy ảnh trên Tab #{0} trong vòng {1} ms: {2}",
                        tabIndex, step.GetInt("timeout_ms", 30000), step.GetStr("image")));
                ctx.Log(Loc.T("msg.image.notfound.continue",
                    "Image not found - continuing"));
            },
        });
    }

    // ---------------------------------------------------------- image exists
    private static void RegisterImageExists()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "image_exists",
            Name = "Image Exists",
            Category = "screen",
            Icon = "search",
            Description = "Single quick check whether the template is on "
                        + "screen (no waiting). Stores the result in the "
                        + "'image_exists' variable for If/Else.",
            ParamSchema =
            {
                ImageSpec, ConfidenceSpec, RegionSpec,
            },
            DefaultParams =
            {
                ["image"] = "", ["confidence"] = 0.9, ["use_region"] = false,
                ["region_x"] = 0, ["region_y"] = 0, ["region_w"] = 0,
                ["region_h"] = 0,
            },
            Summary = p => Loc.F("sum.find_image",
                "{0}  |  timeout {1} ms",
                ShortName(ParamUtil.ToStringValue(p.GetValueOrDefault("image"))), 0) + TabSuffix(p),
            Validate = ValidateImage,
            Execute = (ctx, step) =>
            {
                int tabIndex = ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.find_image.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác tìm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                bool useRegion = step.GetBool("use_region");
                var found = ImageFinder.FindInTab(
                    ctx,
                    tabIndex,
                    step.GetStr("image"),
                    step.GetDouble("confidence", 0.9),
                    1000,
                    useRegion,
                    step.GetInt("region_x"),
                    step.GetInt("region_y"),
                    step.GetInt("region_w"),
                    step.GetInt("region_h"));

                ctx.Variables["last_found_image"] = found.HasValue
                    ? found.Value : null;
                ctx.Variables["image_exists"] = found.HasValue;
                ctx.Log(found.HasValue
                    ? Loc.F("msg.image.found.tab",
                        "Tìm thấy ảnh trên Tab #{0} tại tọa độ giả lập ({1}, {2}), độ khớp {3}",
                        tabIndex, found.Value.Cx, found.Value.Cy, found.Value.Score)
                    : Loc.T("msg.image.notfound.continue",
                        "Image not found - continuing"));
            },
        });
    }

    // -------------------------------------------------------------- screenshot
    private static void RegisterScreenshot()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "screenshot",
            Name = "Screenshot",
            Category = "screen",
            Icon = "camera",
            Description = "Capture the whole desktop or a specific monitor "
                        + "and save it as a PNG file.",
            ParamSchema =
            {
                new("source", Loc.T("param.source", "Capture source"),
                    ParamWidget.Enum)
                {
                    Options = new List<(string, object)>
                    {
                        ("opt.source.virtual", "virtual"),
                        ("opt.source.monitor", "monitor"),
                    },
                    Tooltip = "All monitors combined, or one specific monitor.",
                },
                new("monitor", Loc.T("param.monitor", "Monitor number"),
                    ParamWidget.Int)
                { Min = 1, Max = 8, Tooltip = "Used when source = monitor." },
                new("prefix", Loc.T("param.prefix", "File name prefix"),
                    ParamWidget.Text),
                new("auto_save", Loc.T("param.auto_save", "Auto save"),
                    ParamWidget.Bool)
                { Tooltip = "Save the PNG automatically to the captures folder." },
            },
            DefaultParams =
            {
                ["source"] = "virtual", ["monitor"] = 1,
                ["prefix"] = "screenshot", ["auto_save"] = true,
            },
            Summary = p => Loc.F("sum.screenshot",
                "Save PNG '{0}...'",
                ParamUtil.ToStringValue(p.GetValueOrDefault("prefix"), "screenshot")),
            Execute = (ctx, step) =>
            {
                int tabIndex = ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.screen.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác chụp màn hình. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                var prefix = step.GetStr("prefix", $"tab{tabIndex}_screenshot");
                if (string.IsNullOrWhiteSpace(prefix)) prefix = $"tab{tabIndex}_screenshot";
                var capturesFolder = ScreenshotService.CapturesFolder();
                if (!Directory.Exists(capturesFolder)) Directory.CreateDirectory(capturesFolder);
                var filePath = Path.Combine(capturesFolder, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png");

                bool ok = Macro.Core.Emulator.LdPlayerService.CaptureTabToDisk(tabIndex, filePath);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.screenshot_failed",
                        "Chụp màn hình nền Tab #{0} thất bại. Vui lòng kiểm tra trạng thái tab và kết nối ADB.", tabIndex));
                }
                ctx.Variables["last_screenshot"] = filePath;
                ctx.Log(Loc.F("msg.screenshot.saved", "Screenshot saved: {0}", filePath));
            },
        });
    }

    private static CaptureInfo CaptureMonitor(ActionStep step, string prefix)
    {
        var monitors = ScreenshotService.GetMonitors();
        var index = Math.Max(1, step.GetInt("monitor", 1));
        if (monitors.Count == 0)
            throw new ActionFailureException("No monitor found");
        if (index > monitors.Count) index = monitors.Count;
        return ScreenshotService.CaptureMonitor(index, prefix);
    }

    // ---------------------------------------------------------- pixel color
    private static void RegisterPixelColor()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "get_pixel_color",
            Name = "Get Pixel Color",
            Category = "screen",
            Icon = "pixel",
            Description = "Read the color of one pixel and show it in the log.",
            ParamSchema =
            {
                new("position", Loc.T("param.position", "Position"),
                    ParamWidget.Position)
                { Tooltip = "Pixel coordinates to read." },
            },
            DefaultParams = { ["x"] = 500, ["y"] = 300 },
            Summary = p => Loc.F("sum.pixel", "Color at ({0}, {1})",
                ParamUtil.ToInt(p.GetValueOrDefault("x")),
                ParamUtil.ToInt(p.GetValueOrDefault("y"))),
            Execute = (ctx, step) =>
            {
                int tabIndex = ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.screen.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác lấy màu điểm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                int x = step.GetInt("x");
                int y = step.GetInt("y");

                var cap = Macro.Core.Emulator.LdPlayerService.CaptureTabMat(tabIndex);
                if (!cap.Success || cap.Frame == null)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.getpixel_failed",
                        "Không thể lấy màu điểm ảnh trên Tab #{0}: {1}", tabIndex, cap.ErrorMessage));
                }
                using (cap.Frame)
                {
                    if (x < 0 || x >= cap.Frame.Width || y < 0 || y >= cap.Frame.Height)
                    {
                        throw new ActionFailureException(Loc.F("msg.pixel.outofbounds",
                            "Tọa độ ({0}, {1}) nằm ngoài kích thước màn hình Tab #{2} ({3}x{4})",
                            x, y, tabIndex, cap.Frame.Width, cap.Frame.Height));
                    }
                    var vec = cap.Frame.At<OpenCvSharp.Vec3b>(y, x); // BGR
                    string colorHex = $"#{vec.Item2:X2}{vec.Item1:X2}{vec.Item0:X2}";
                    ctx.Variables["last_pixel_color"] = colorHex;
                    ctx.Variables["pixel_r"] = (int)vec.Item2;
                    ctx.Variables["pixel_g"] = (int)vec.Item1;
                    ctx.Variables["pixel_b"] = (int)vec.Item0;
                    ctx.Log(Loc.F("msg.pixel.at", "Pixel at ({0}, {1}) on Tab #{2} = {3}",
                        x, y, tabIndex, colorHex));
                }
            },
        });
    }
}
