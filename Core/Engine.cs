using System.Diagnostics;
using System.Threading;

namespace Macro.Core;

public enum EngineState { Idle, Running, Paused, Stopping }

/// <summary>
/// Shared state passed to every action while a workflow runs. Offers an
/// interruptible Sleep and Pause support (a pause freezes waits instead of
/// consuming them).
/// </summary>
public class ExecutionContext
{
    private readonly CancellationToken _token;
    private readonly ManualResetEventSlim _resume;
    private readonly Action<string> _log;

    public Workflow Workflow { get; set; }
    public string WorkflowFilePath { get; set; }
    public Dictionary<string, object> Variables = new();
    public int LoopDepth;
    public int JumpCount;
    public const int MaxJumps = 100_000;

    public ExecutionContext(CancellationToken token,
                            ManualResetEventSlim resume,
                            Action<string> log)
    {
        _token = token;
        _resume = resume;
        _log = log;
    }

    public void Log(string message) => _log(message);

    /// <summary>Throw StopRequested if the user pressed Stop.</summary>
    public void CheckStop()
    {
        try { _token.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException) { throw new StopRequestedException(); }
    }

    /// <summary>Block while paused. Wakes up for Stop.</summary>
    public void WaitIfPaused()
    {
        if (!_resume.IsSet)
            try { _resume.Wait(_token); }
            catch (OperationCanceledException) { throw new StopRequestedException(); }
    }

    private void Checkpoint()
    {
        CheckStop();
        WaitIfPaused();
    }

    /// <summary>Interruptible sleep (50 ms slices, pause freezes countdown).</summary>
    public void Sleep(double seconds)
    {
        var remaining = Math.Max(0, seconds);
        var watch = Stopwatch.StartNew();
        while (remaining > 0)
        {
            Checkpoint();
            if (!_resume.IsSet)
            {
                Thread.Sleep(50);
                continue;
            }
            watch.Restart();
            Thread.Sleep((int)Math.Min(50, remaining * 1000));
            remaining -= watch.Elapsed.TotalSeconds;
        }
    }
}

/// <summary>
/// Execution Engine: runs a Workflow on a background thread so the UI stays
/// responsive. Supports Run / Pause / Resume / Stop and reports every step
/// through events (raised on the worker thread - the UI must marshal them).
/// </summary>
public sealed class ExecutionEngine
{
    private Thread _thread;
    private CancellationTokenSource _cts;
    private ManualResetEventSlim _resume = new(true);

    public EngineState State { get; private set; } = EngineState.Idle;
    public Workflow CurrentWorkflow { get; private set; }
    public Workflow Workflow
    {
        get => CurrentWorkflow;
        set => CurrentWorkflow = value;
    }
    public string WorkflowFilePath { get; set; }
    public int TargetTabIndex { get; set; } = -1;

    public event Action<EngineState> StateChanged;
    public event Action<string> StepStarted;      // step id
    public event Action<string> StepDone;         // step id
    public event Action<string> LogMessage;
    public event Action<bool, string> Finished;   // success, message

    public bool IsRunning => State is EngineState.Running
                                or EngineState.Paused
                                or EngineState.Stopping;

    public static bool RequireEmulator { get; set; } = true;
    public static readonly object InputLock = new();

    public bool Run(Workflow workflow)
    {
        if (IsRunning) return false;

        if (RequireEmulator && !Macro.Core.Emulator.EmulatorManager.IsConfiguredAndValid)
        {
            LogMessage?.Invoke("[Lỗi] Quy trình không thể khởi chạy: Trình giả lập Android chưa được cấu hình hoặc đường dẫn không hợp lệ.");
            Finished?.Invoke(false, Macro.Localization.Loc.T("emu.err.cannot_run", "Chưa cấu hình trình giả lập Android hợp lệ."));
            return false;
        }

        CurrentWorkflow = workflow;
        _cts = new CancellationTokenSource();
        _resume = new ManualResetEventSlim(true);
        SetState(EngineState.Running);
        _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "macro-engine" };
        _thread.Start();
        return true;
    }

    public void Pause()
    {
        if (State != EngineState.Running) return;
        _resume.Reset();
        SetState(EngineState.Paused);
    }

    public void Resume()
    {
        if (State != EngineState.Paused) return;
        _resume.Set();
        SetState(EngineState.Running);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _resume.Set();          // wake a paused run so it can exit
        _cts.Cancel();
        SetState(EngineState.Stopping);
    }

    private void SetState(EngineState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    // ---------------------------------------------------------------- worker
    private void WorkerLoop()
    {
        var ctx = new ExecutionContext(_cts.Token, _resume,
                                       msg => LogMessage?.Invoke(msg))
        {
            Workflow = CurrentWorkflow,
            WorkflowFilePath = WorkflowFilePath,
        };
        var wf = CurrentWorkflow;
        try
        {
            int tabIdx = TargetTabIndex;
            if (tabIdx < 0 && Macro.Core.Emulator.EmulatorManager.SelectedInstance != null)
            {
                tabIdx = Macro.Core.Emulator.EmulatorManager.SelectedInstance.Index;
            }
            if (tabIdx >= 0)
            {
                ctx.Variables["tab_index"] = tabIdx;
            }
            else if (RequireEmulator)
            {
                var live = Macro.Core.Emulator.EmulatorManager.GetLiveInstances().Where(i => i.IsRunning).ToList();
                if (live.Count == 1)
                {
                    tabIdx = live[0].Index;
                    ctx.Variables["tab_index"] = tabIdx;
                    Macro.Core.Emulator.EmulatorManager.SelectedInstance = live[0];
                }
                else
                {
                    var errMsg = live.Count == 0
                        ? Localization.Loc.T("emu.err.no_running_tab", "Không có tab giả lập LDPlayer nào đang chạy. Vui lòng khởi động tab trước khi chạy macro.")
                        : Localization.Loc.T("emu.err.multi_tab_ambiguous", "Chưa xác định tab giả lập LDPlayer cụ thể để chạy macro. Vui lòng chọn một tab.");
                    ctx.Log($"[Lỗi] {errMsg}");
                    Finish(false, errMsg);
                    return;
                }
            }

            int repeats = wf.Settings.TryGetValue("repeat_workflow", out var rp)
                ? ParamUtil.ToInt(rp, 1) : 1;
            if (repeats < 0) repeats = 1;

            if (repeats == 0)
            {
                ctx.Log(Localization.Loc.F("msg.workflow.infinite",
                    "Quy trình '{0}' bắt đầu (Lặp vô hạn cho đến khi dừng)", wf.Name));
                int round = 0;
                while (true)
                {
                    ctx.CheckStop();
                    ctx.WaitIfPaused();
                    round++;
                    ctx.Log(Localization.Loc.F("msg.workflow.infinite_round",
                        "--- Vòng lặp #{0} (Vô hạn) ---", round));
                    RunList(ctx, wf.Actions);
                }
            }
            else
            {
                ctx.Log(repeats > 1
                    ? Localization.Loc.F("msg.workflow.repeats",
                        "Workflow '{0}' started ({1}x)", wf.Name, repeats)
                    : Localization.Loc.F("msg.workflow.started",
                        "Workflow '{0}' started", wf.Name));
                for (var round = 0; round < repeats; round++)
                {
                    ctx.CheckStop();
                    ctx.WaitIfPaused();
                    if (repeats > 1)
                    {
                        ctx.Log(Localization.Loc.F("msg.workflow.round",
                            "--- Vòng lặp #{0}/{1} ---", round + 1, repeats));
                    }
                    RunList(ctx, wf.Actions);
                }
            }
            ctx.Log(Localization.Loc.T("msg.workflow.finished",
                "Workflow finished successfully"));
            Finish(true, Localization.Loc.T("msg.workflow.finished",
                "Workflow finished"));
        }
        catch (StopRequestedException ex)
        {
            var message = ex.Message.Length > 0
                ? ex.Message
                : Localization.Loc.T("msg.stopped.by.user", "Stopped by user");
            ctx.Log(message);
            Finish(false, message);
        }
        catch (OperationCanceledException)
        {
            var message = Localization.Loc.T("msg.stopped.by.user",
                "Stopped by user");
            ctx.Log(message);
            Finish(false, message);
        }
        catch (BreakLoopException)
        {
            var message = Localization.Loc.T("msg.break.outside",
                "'Break' was used outside of a Loop");
            ctx.Log(message);
            Finish(false, message);
        }
        catch (GotoStepException ex)
        {
            var message = Localization.Loc.F("msg.goto.not_found",
                "Không tìm thấy lệnh đích '{0}' trong quy trình.", ex.TargetStepId);
            ctx.Log(message);
            Finish(false, message);
        }
        catch (ActionFailureException ex)
        {
            ctx.Log(ex.Message);
            Finish(false, ex.Message);
        }
        catch (Exception ex)     // unexpected bug - surface it clearly
        {
            var message = Localization.Loc.F("msg.unexpected.error",
                "Unexpected error: {0}", ex.Message);
            ctx.Log(message);
            Finish(false, message);
        }
    }

    private void Finish(bool ok, string message)
    {
        SetState(EngineState.Idle);
        Finished?.Invoke(ok, message);
    }

    public void RunWorkflowInline(Workflow wf, ExecutionContext ctx)
    {
        CurrentWorkflow = wf;
        RunList(ctx, wf.Actions);
    }

    public void RunList(ExecutionContext ctx, List<ActionStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            ctx.CheckStop();
            ctx.WaitIfPaused();
            if (!step.Enabled) continue;

            var def = ActionRegistry.Get(step.Type);
            if (def == null)
                throw new ActionFailureException(
                    Localization.Loc.F("msg.unknown.type",
                        "Unknown action type '{0}'", step.Type));

            StepStarted?.Invoke(step.Id);
            var label = string.IsNullOrEmpty(step.Name)
                ? Localization.Loc.ActionName(step.Type) : step.Name;
            try
            {
                if (def.Validate is { } validate)
                {
                    var error = validate(step);
                    if (error != null)
                        throw new ActionFailureException(error);
                }
                if (def.IsContainer)
                    RunContainer(ctx, def, step);
                else
                {
                    lock (InputLock)
                    {
                        def.Execute(ctx, step);
                    }
                }
            }
            catch (StopRequestedException) { throw; }
            catch (BreakLoopException) { throw; }
            catch (GotoStepException gotoEx)
            {
                StepDone?.Invoke(step.Id);
                ctx.JumpCount++;
                if (ctx.JumpCount > ExecutionContext.MaxJumps)
                    throw new ActionFailureException(Localization.Loc.T("msg.goto.max_jumps",
                        "Đã vượt quá giới hạn chuyển lệnh tối đa (100,000 lần). Dừng quy trình để tránh lặp vô hạn."));

                int targetIdx = steps.FindIndex(s => s.Id == gotoEx.TargetStepId);
                if (targetIdx < 0)
                {
                    targetIdx = steps.FindIndex(s => s.Walk().Any(w => w.Id == gotoEx.TargetStepId));
                }

                if (targetIdx >= 0)
                {
                    ctx.Log(Localization.Loc.F("msg.goto.jump",
                        "Chuyển luồng đến lệnh #{0} (ID: {1})", targetIdx + 1, gotoEx.TargetStepId));
                    i = targetIdx - 1;
                    continue;
                }
                throw;
            }
            catch (ActionFailureException ex)
            {
                throw new ActionFailureException($"Step '{label}' failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new ActionFailureException($"Step '{label}' failed: {ex.Message}");
            }
            StepDone?.Invoke(step.Id);

            double delayMs = wf_delay(CurrentWorkflow);
            if (delayMs > 0) ctx.Sleep(delayMs / 1000.0);
        }
    }

    private static double wf_delay(Workflow wf) =>
        wf.Settings.TryGetValue("step_delay_ms", out var value)
            ? ParamUtil.ToInt(value, 0) : 0;

    private void RunContainer(ExecutionContext ctx, ActionDefinition def,
                              ActionStep step)
    {
        // ---- If/Else: evaluate the condition, run the matching branch once
        if (def.TypeId == "if_else")
        {
            var condition = EvaluateCondition(ctx, step, out var result);
            var branch = result ? step.Children : step.ElseChildren;
            ctx.Log(Localization.Loc.F("msg.condition",
                "Condition '{0}' {1} '{2}' => {3}",
                condition.Variable, condition.OpLabel, condition.Value,
                result
                    ? Localization.Loc.T("msg.if.then", "true - running THEN branch")
                    : (step.ElseChildren.Count > 0
                        ? Localization.Loc.T("msg.if.else", "false - running ELSE branch")
                        : Localization.Loc.T("msg.if.no.else",
                            "false - no ELSE branch, skipping"))));
            // BreakLoopException propagates to the enclosing Loop (correct
            // semantics for "break inside an if inside a loop").
            RunList(ctx, branch);
            return;
        }

        // ---- Loop / Repeat
        int times = Math.Max(1, step.GetInt("times", 1));
        double betweenMs = step.GetDouble("iteration_delay_ms", 0);
        var label = string.IsNullOrEmpty(step.Name)
            ? Localization.Loc.ActionName(step.Type) : step.Name;

        ctx.LoopDepth++;
        try
        {
            for (var iteration = 0; iteration < times; iteration++)
            {
                ctx.CheckStop();
                ctx.WaitIfPaused();
                if (times > 1)
                    ctx.Log(Localization.Loc.F("msg.iteration",
                        "{0}: iteration {1}/{2}", label, iteration + 1, times));
                try { RunList(ctx, step.Children); }
                catch (BreakLoopException)
                {
                    ctx.Log(Localization.Loc.F("msg.breakloop", "{0}: break", label));
                    break;
                }
                if (iteration < times - 1 && betweenMs > 0)
                    ctx.Sleep(betweenMs / 1000.0);
            }
        }
        finally
        {
            ctx.LoopDepth--;
        }
    }

    private static (string Variable, string OpLabel, string Value) EvaluateCondition(
        ExecutionContext ctx, ActionStep step, out bool result)
    {
        var variable = step.GetStr("variable").Trim();
        var op = step.GetStr("op", "exists");
        var value = step.GetStr("value");

        var hasVariable = ctx.Variables.TryGetValue(variable, out var raw)
                          && raw != null;
        var left = hasVariable ? ParamUtil.ToStringValue(raw) : "";

        switch (op)
        {
            case "not_exists":
                result = !hasVariable || left.Length == 0;
                break;
            case "equals":
                result = string.Equals(left.Trim(), value.Trim(),
                                       StringComparison.OrdinalIgnoreCase);
                break;
            case "not_equals":
                result = !string.Equals(left.Trim(), value.Trim(),
                                        StringComparison.OrdinalIgnoreCase);
                break;
            case "contains":
                result = hasVariable
                         && left.Contains(value, StringComparison.OrdinalIgnoreCase);
                break;
            case "greater":
            case "less":
                var leftNumber = ParamUtil.ToDouble(hasVariable ? left : null,
                                                     double.NaN);
                var rightNumber = ParamUtil.ToDouble(value, double.NaN);
                result = op == "greater"
                    ? leftNumber > rightNumber
                    : leftNumber < rightNumber;
                break;
            default:    // "exists"
                result = hasVariable && left.Length > 0;
                break;
        }
        return (variable,
                Localization.Loc.T("op." + op, op), value);
    }
}
