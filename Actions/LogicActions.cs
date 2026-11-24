using Macro.Core;
using Macro.Localization;

namespace Macro.Actions;

/// <summary>Flow control actions: If/Else, Loop, Repeat, Break,
/// Stop Workflow.</summary>
public static class LogicActions
{
    private static ParamSpec TimesSpec => new(
        "times", Loc.T("param.times", "Repeat"), ParamWidget.Int)
    { Min = 1, Max = 100000, Unit = "times" };

    private static ParamSpec IterationDelaySpec => new(
        "iteration_delay_ms",
        Loc.T("param.iteration_delay_ms", "Delay between iterations"),
        ParamWidget.Int)
    { Min = 0, Max = 600000, Unit = "ms" };

    private static ParamSpec VariableSpec => new(
        "variable", Loc.T("param.variable", "Variable name"), ParamWidget.Text)
    {
        Tooltip = "Name of a variable stored by a previous action, e.g. "
                + "image_exists, last_pixel_color...",
    };

    public static ParamSpec OperatorSpec => new(
        "op", Loc.T("param.op", "Operator"), ParamWidget.Enum)
    {
        Options = new List<(string, object)>
        {
            ("op.exists", "exists"),
            ("op.not_exists", "not_exists"),
            ("op.equals", "equals"),
            ("op.not_equals", "not_equals"),
            ("op.contains", "contains"),
            ("op.greater", "greater"),
            ("op.less", "less"),
        },
    };

    public static void Register()
    {
        RegisterIfElse();
        RegisterLoop();
        RegisterRepeat();
        RegisterBreak();
        RegisterGotoStep();
        RegisterStopWorkflow();
    }

    // The If/Else container: the engine evaluates the condition and runs
    // Children (THEN) or ElseChildren (ELSE).
    private static void RegisterIfElse()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "if_else",
            Name = "If / Else",
            Category = "logic",
            Icon = "break",
            Description = "Check a condition on a variable: true runs the "
                        + "THEN steps, false runs the ELSE steps.",
            IsContainer = true,
            ParamSchema = { VariableSpec, OperatorSpec,
                new("value", Loc.T("param.value", "Compare value"),
                    ParamWidget.Text)
                {
                    Tooltip = "Text/number to compare with. Leave empty for "
                            + "exists / not_exists.",
                },
            },
            DefaultParams = { ["variable"] = "image_exists", ["op"] = "exists",
                              ["value"] = "" },
            Summary = p => Loc.F("sum.if_else",
                "If [{0} {1} '{2}']",
                ParamUtil.ToStringValue(p.GetValueOrDefault("variable")),
                Loc.T("op." + ParamUtil.ToStringValue(p.GetValueOrDefault("op"), "exists"),
                      ParamUtil.ToStringValue(p.GetValueOrDefault("op"), "exists")),
                ParamUtil.ToStringValue(p.GetValueOrDefault("value"))),
        });
    }

    // The Loop container: the engine repeats its children.
    private static void RegisterLoop()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "loop",
            Name = "Loop",
            Category = "logic",
            Icon = "loop",
            Description = "Repeat the steps placed inside this block.",
            IsContainer = true,
            ParamSchema = { TimesSpec, IterationDelaySpec },
            DefaultParams = { ["times"] = 5, ["iteration_delay_ms"] = 0 },
            Summary = p =>
            {
                var times = ParamUtil.ToInt(p.GetValueOrDefault("times"), 1);
                var delay = ParamUtil.ToInt(p.GetValueOrDefault("iteration_delay_ms"));
                return delay > 0
                    ? Loc.F("sum.loop.delay", "Repeat {0}x, {1} ms between",
                            times, delay)
                    : Loc.F("sum.loop", "Repeat {0}x", times);
            },
        });
    }

    private static void RegisterRepeat()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "repeat",
            Name = "Repeat",
            Category = "logic",
            Icon = "loop",
            Description = "Repeat the group of steps inside N times "
                        + "(same behavior as Loop).",
            IsContainer = true,
            ParamSchema = { TimesSpec, IterationDelaySpec },
            DefaultParams = { ["times"] = 3, ["iteration_delay_ms"] = 0 },
            Summary = p => Loc.F("sum.repeat", "Repeat {0}x",
                ParamUtil.ToInt(p.GetValueOrDefault("times"), 1)),
        });
    }

    private static void RegisterBreak()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "break",
            Name = "Break",
            Category = "logic",
            Icon = "break",
            Description = "Exit the closest Loop immediately.",
            Summary = _ => Loc.T("sum.break", "Exit current loop"),
            Execute = (ctx, step) =>
            {
                if (ctx.LoopDepth <= 0)
                    throw new ActionFailureException(Loc.T("msg.break.only.loop",
                        "Break can only be used inside a Loop"));
                throw new BreakLoopException();
            },
        });
    }

    private static void RegisterStopWorkflow()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "stop_workflow",
            Name = "Stop Workflow",
            Category = "logic",
            Icon = "stop",
            Description = "Stop the whole workflow at this step (like "
                        + "pressing Stop).",
            Summary = _ => Loc.T("sum.stop", "Stop the whole workflow"),
            Execute = (ctx, step) =>
            {
                ctx.Log(Loc.T("msg.stop.by.workflow",
                    "Workflow stopped by the Stop Workflow action"));
                throw new StopRequestedException(Loc.T("msg.stop.by.workflow",
                    "Workflow stopped by the Stop Workflow action"));
            },
        });
    }

    private static void RegisterGotoStep()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "goto_step",
            Name = "Chuyển đến dòng lệnh",
            Category = "logic",
            Icon = "goto",
            Description = "Chuyển luồng thực thi đến một lệnh được chọn trong danh sách.",
            ParamSchema =
            {
                new("target_step_id", Loc.T("param.target_step", "Lệnh đích"),
                    ParamWidget.StepSelector)
                {
                    Tooltip = "Chọn lệnh muốn chuyển đến trong danh sách quy trình.",
                },
            },
            DefaultParams = { ["target_step_id"] = "" },
            Summary = p =>
            {
                var targetId = ParamUtil.ToStringValue(p.GetValueOrDefault("target_step_id"));
                return string.IsNullOrEmpty(targetId)
                    ? Loc.T("sum.goto.empty", "Chưa chọn lệnh đích")
                    : Loc.F("sum.goto.target", "Chuyển đến {0}", targetId);
            },
            Validate = step =>
            {
                var target = step.GetStr("target_step_id");
                if (string.IsNullOrWhiteSpace(target))
                    return Loc.T("val.goto.no_target", "Chưa chọn dòng lệnh đích.");
                if (target == step.Id)
                    return Loc.T("val.goto.self_target", "Không thể chọn chuyển đến chính dòng lệnh này.");
                return null;
            },
            Execute = (ctx, step) =>
            {
                var target = step.GetStr("target_step_id");
                if (string.IsNullOrWhiteSpace(target))
                    throw new ActionFailureException(Loc.T("val.goto.no_target", "Chưa chọn dòng lệnh đích."));
                if (target == step.Id)
                    throw new ActionFailureException(Loc.T("val.goto.self_target", "Không thể chọn chuyển đến chính dòng lệnh này."));
                ctx.Log(Loc.F("msg.goto.executing", "Chuyển luồng thực thi đến lệnh {0}", target));
                throw new GotoStepException(target);
            },
        });
    }
}

