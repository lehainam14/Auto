using Macro.Core;
using Macro.Localization;

namespace Macro.Actions;

/// <summary>Timing actions: wait a fixed or random duration.</summary>
public static class TimingActions
{
    public static void Register()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "wait",
            Name = "Wait",
            Category = "timing",
            Icon = "wait",
            Description = "Pause the workflow for a fixed duration.",
            ParamSchema =
            {
                new("duration_ms", Loc.T("param.duration_ms", "Duration"),
                    ParamWidget.Int)
                { Min = 0, Max = 600000, Unit = "ms" },
            },
            DefaultParams = { ["duration_ms"] = 1000 },
            Summary = p => Loc.F("sum.wait", "Wait {0} ms",
                ParamUtil.ToInt(p.GetValueOrDefault("duration_ms"), 1000)),
            Execute = (ctx, step) =>
                ctx.Sleep(step.GetInt("duration_ms", 1000) / 1000.0),
        });

        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "random_wait",
            Name = "Random Wait",
            Category = "timing",
            Icon = "random",
            Description = "Pause for a random duration between min and max "
                        + "(more human-like than a fixed wait).",
            ParamSchema =
            {
                new("min_ms", Loc.T("param.min_ms", "Minimum"), ParamWidget.Int)
                { Min = 0, Max = 600000, Unit = "ms" },
                new("max_ms", Loc.T("param.max_ms", "Maximum"), ParamWidget.Int)
                { Min = 0, Max = 600000, Unit = "ms" },
            },
            DefaultParams = { ["min_ms"] = 500, ["max_ms"] = 1500 },
            Summary = p => Loc.F("sum.random_wait", "Wait {0}-{1} ms",
                ParamUtil.ToInt(p.GetValueOrDefault("min_ms"), 500),
                ParamUtil.ToInt(p.GetValueOrDefault("max_ms"), 1500)),
            Execute = (ctx, step) =>
            {
                var lo = step.GetDouble("min_ms", 500);
                var hi = step.GetDouble("max_ms", 1500);
                if (hi < lo) (lo, hi) = (hi, lo);
                ctx.Sleep((Random.Shared.NextDouble() * (hi - lo) + lo) / 1000.0);
            },
        });
    }
}
