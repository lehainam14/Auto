using Macro.Core;
using Macro.Core.Emulator;
using Macro.Core.Input;
using Macro.Localization;

namespace Macro.Actions;

/// <summary>Mouse actions: click, double click, right click, move, drag,
/// scroll.</summary>
public static class MouseActions
{
    private static ParamSpec Position(string tooltipKey, string tooltipEn) => new(
        "position", Loc.T("param.position", "Position"), ParamWidget.Position)
    { Tooltip = Loc.T(tooltipKey, tooltipEn) };

    private static ParamSpec ButtonSpec => new(
        "button", Loc.T("param.button", "Mouse button"), ParamWidget.Enum)
    {
        Options = new List<(string, object)>
        {
            ("opt.left", "left"), ("opt.middle", "middle"), ("opt.right", "right"),
        },
    };

    private static string ButtonLabel(object value) =>
        Loc.T("opt." + ParamUtil.ToStringValue(value, "left"),
              ParamUtil.ToStringValue(value, "left"));

    public static void Register()
    {
        RegisterClick();
        RegisterDoubleClick();
        RegisterRightClick();
        RegisterMove();
        RegisterDrag();
        RegisterScroll();
    }

    public static (int X, int Y) ResolvePoint(ActionStep step, int localX, int localY)
    {
        if (step != null && step.Params.TryGetValue("tab_index", out var tiObj))
        {
            int tabIndex = ParamUtil.ToInt(tiObj, -1);
            if (tabIndex >= 0)
            {
                var resolved = LdPlayerService.TranslateTabToScreen(tabIndex, localX, localY);
                if (resolved.HasValue)
                    return (resolved.Value.ScreenX, resolved.Value.ScreenY);
            }
        }
        return (localX, localY);
    }

    private static string TabSuffix(IDictionary<string, object> p) =>
        p.TryGetValue("tab_index", out var ti) && ParamUtil.ToInt(ti, -1) >= 0
            ? $" [Tab #{ParamUtil.ToInt(ti)}]"
            : "";

    // ---------------------------------------------------------------- click
    private static void RegisterClick()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "click",
            Name = "Click",
            Category = "mouse",
            Icon = "click",
            Description = "Click the mouse at a position on the screen.",
            ParamSchema = { Position("action.click.desc", "Where to click."),
                            new("clicks", "Clicks", ParamWidget.Int)
                            { Min = 1, Max = 10, Unit = "times" },
                            ButtonSpec },
            DefaultParams = { ["x"] = 100, ["y"] = 100, ["clicks"] = 1,
                              ["button"] = "left" },
            Summary = p =>
            {
                var clicks = ParamUtil.ToInt(p.GetValueOrDefault("clicks"), 1);
                var text = Loc.F("sum.click", "{0} click at ({1}, {2})",
                    ButtonLabel(p.GetValueOrDefault("button")),
                    ParamUtil.ToInt(p.GetValueOrDefault("x")),
                    ParamUtil.ToInt(p.GetValueOrDefault("y"))) + TabSuffix(p);
                return clicks > 1
                    ? $"{text} {Loc.F("sum.clicks", "x{0} times", clicks)}"
                    : text;
            },
            Execute = (ctx, step) =>
            {
                int tabIndex = LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.mouse.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác click chuột. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                int x = step.GetInt("x");
                int y = step.GetInt("y");
                string button = step.GetStr("button", "left");
                int clicks = Math.Max(1, step.GetInt("clicks", 1));

                bool ok = LdPlayerService.ClickTab(tabIndex, x, y, button, clicks);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.click_failed",
                        "Gửi thao tác click vào Tab #{0} tại ({1}, {2}) thất bại. Vui lòng kiểm tra trạng thái tab và kết nối ADB.",
                        tabIndex, x, y));
                }
                ctx.Log(Loc.F("msg.tab.clicked", "Đã click nền tại ({0}, {1}) trên Tab #{2}", x, y, tabIndex));
            },
        });
    }

    private static void RegisterDoubleClick()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "double_click",
            Name = "Double Click",
            Category = "mouse",
            Icon = "dblclick",
            Description = "Double click the mouse at a position on the screen.",
            ParamSchema = { Position("action.double_click.desc", "Where to double click.") },
            DefaultParams = { ["x"] = 100, ["y"] = 100 },
            Summary = p => Loc.F("sum.double_click",
                "Double click at ({0}, {1})",
                ParamUtil.ToInt(p.GetValueOrDefault("x")),
                ParamUtil.ToInt(p.GetValueOrDefault("y"))) + TabSuffix(p),
            Execute = (ctx, step) =>
            {
                int tabIndex = LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.mouse.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác double click. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                int x = step.GetInt("x");
                int y = step.GetInt("y");
                bool ok = LdPlayerService.ClickTab(tabIndex, x, y, "left", 2);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.dblclick_failed",
                        "Gửi thao tác double click vào Tab #{0} tại ({1}, {2}) thất bại.", tabIndex, x, y));
                }
                ctx.Log(Loc.F("msg.tab.dblclicked", "Đã double click nền tại ({0}, {1}) trên Tab #{2}", x, y, tabIndex));
            },
        });
    }

    private static void RegisterRightClick()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "right_click",
            Name = "Right Click",
            Category = "mouse",
            Icon = "rightclick",
            Description = "Right click the mouse at a position on the screen.",
            ParamSchema = { Position("action.right_click.desc", "Where to right click.") },
            DefaultParams = { ["x"] = 100, ["y"] = 100 },
            Summary = p => Loc.F("sum.right_click",
                "Right click at ({0}, {1})",
                ParamUtil.ToInt(p.GetValueOrDefault("x")),
                ParamUtil.ToInt(p.GetValueOrDefault("y"))) + TabSuffix(p),
            Execute = (ctx, step) =>
            {
                int tabIndex = LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.mouse.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác right click. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                int x = step.GetInt("x");
                int y = step.GetInt("y");
                bool ok = LdPlayerService.ClickTab(tabIndex, x, y, "right");
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.rightclick_failed",
                        "Gửi thao tác right click vào Tab #{0} tại ({1}, {2}) thất bại.", tabIndex, x, y));
                }
                ctx.Log(Loc.F("msg.tab.rightclicked", "Đã right click nền tại ({0}, {1}) trên Tab #{2}", x, y, tabIndex));
            },
        });
    }

    // ----------------------------------------------------------------- move
    private static void RegisterMove()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "move_mouse",
            Name = "Move Mouse",
            Category = "mouse",
            Icon = "move",
            Description = "Move the mouse pointer to a position, optionally animated.",
            ParamSchema =
            {
                Position("action.move_mouse.desc", "Where to move the pointer."),
                new("duration_ms", "Move duration", ParamWidget.Int)
                { Min = 0, Max = 10000, Unit = "ms",
                  Tooltip = "0 moves instantly; larger values animate the move." },
            },
            DefaultParams = { ["x"] = 500, ["y"] = 300, ["duration_ms"] = 0 },
            Summary = p =>
            {
                var ms = ParamUtil.ToInt(p.GetValueOrDefault("duration_ms"));
                var tabInfo = TabSuffix(p);
                return ms > 0
                    ? Loc.F("sum.move.over", "Move to ({0}, {1}) over {2} ms",
                        ParamUtil.ToInt(p.GetValueOrDefault("x")),
                        ParamUtil.ToInt(p.GetValueOrDefault("y")), ms) + tabInfo
                    : Loc.F("sum.move", "Move to ({0}, {1})",
                        ParamUtil.ToInt(p.GetValueOrDefault("x")),
                        ParamUtil.ToInt(p.GetValueOrDefault("y"))) + tabInfo;
            },
            Execute = (ctx, step) =>
            {
                int tabIndex = LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.mouse.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác di chuyển chuột. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                ctx.Log(Loc.F("msg.tab.moved", "Vị trí con trỏ trên Tab #{0}: ({1}, {2})",
                    tabIndex, step.GetInt("x"), step.GetInt("y")));
            },
        });
    }

    // ----------------------------------------------------------------- drag
    private static void RegisterDrag()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "drag",
            Name = "Drag and Drop",
            Category = "mouse",
            Icon = "drag",
            Description = "Drag from one position to another while holding a button.",
            ParamSchema =
            {
                new("from", Loc.T("param.from", "From"), ParamWidget.Position)
                { Tooltip = "Start point." },
                new("to", Loc.T("param.to", "To"), ParamWidget.Position)
                { Tooltip = "End point." },
                new("duration_ms", Loc.T("param.duration_ms", "Drag duration"),
                    ParamWidget.Int)
                { Min = 0, Max = 10000, Unit = "ms" },
                ButtonSpec,
            },
            DefaultParams = { ["x1"] = 100, ["y1"] = 100, ["x2"] = 500,
                              ["y2"] = 400, ["duration_ms"] = 300,
                              ["button"] = "left" },
            Summary = p => Loc.F("sum.drag", "Drag ({0}, {1}) to ({2}, {3})",
                ParamUtil.ToInt(p.GetValueOrDefault("x1")),
                ParamUtil.ToInt(p.GetValueOrDefault("y1")),
                ParamUtil.ToInt(p.GetValueOrDefault("x2")),
                ParamUtil.ToInt(p.GetValueOrDefault("y2"))) + TabSuffix(p),
            Execute = (ctx, step) =>
            {
                int tabIndex = LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.mouse.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác kéo thả. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                int x1 = step.GetInt("x1");
                int y1 = step.GetInt("y1");
                int x2 = step.GetInt("x2");
                int y2 = step.GetInt("y2");
                int dur = step.GetInt("duration_ms", 300);

                bool ok = LdPlayerService.DragTab(tabIndex, x1, y1, x2, y2, dur);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.drag_failed",
                        "Gửi thao tác kéo thả trên Tab #{0} từ ({1}, {2}) tới ({3}, {4}) thất bại.",
                        tabIndex, x1, y1, x2, y2));
                }
                ctx.Log(Loc.F("msg.tab.dragged", "Đã kéo thả nền từ ({0}, {1}) tới ({2}, {3}) trên Tab #{4}",
                    x1, y1, x2, y2, tabIndex));
            },
        });
    }

    // --------------------------------------------------------------- scroll
    private static void RegisterScroll()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "scroll",
            Name = "Scroll",
            Category = "mouse",
            Icon = "scroll",
            Description = "Scroll the mouse wheel at the current position.",
            ParamSchema =
            {
                new("amount", Loc.T("param.amount", "Amount"), ParamWidget.Int)
                { Min = -100, Max = 100, Unit = "notches",
                  Tooltip = "Positive scrolls up, negative scrolls down." },
            },
            DefaultParams = { ["amount"] = 3 },
            Summary = p =>
            {
                var amount = ParamUtil.ToInt(p.GetValueOrDefault("amount"));
                return amount >= 0
                    ? Loc.F("sum.scroll.up", "Scroll up by {0}", amount)
                    : Loc.F("sum.scroll.down", "Scroll down by {0}", Math.Abs(amount));
            },
            Execute = (ctx, step) =>
            {
                var amount = step.GetInt("amount");
                if (amount == 0)
                    throw new ActionFailureException(Loc.T("msg.cannot.scroll0",
                        "Scroll amount cannot be 0"));

                int tabIndex = LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.mouse.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác cuộn. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                int x = step.GetInt("x", 480);
                int y = step.GetInt("y", 270);
                bool ok = LdPlayerService.ScrollTab(tabIndex, x, y, amount);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.scroll_failed",
                        "Gửi thao tác cuộn trên Tab #{0} thất bại.", tabIndex));
                }
                ctx.Log(Loc.F("msg.tab.scrolled", "Đã cuộn màn hình nền trên Tab #{0} (lượng: {1})", tabIndex, amount));
            },
        });
    }
}
