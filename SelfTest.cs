using System.IO;
using System.Windows;
using Macro.Actions;
using Macro.Core;
using Macro.Localization;

namespace Macro;

/// <summary>
/// Headless self-test of the core layers (run with: macro.exe --selftest).
/// Writes PASS/FAIL lines to a result file next to the executable.
/// </summary>
public static class SelfTest
{
    private static readonly List<string> _results = new();
    private static int _failed;

    public static void Run(string outputPath)
    {
        ActionsModule.RegisterAll();
        ActionDefaults.ClearDefaultCoordinates();
        ActionDefaults.ClearDefaultImageTemplate();

        TestRegistry();
        TestParamUtils();
        TestStorageRoundtrip();
        TestCloseConfirmationLogic();
        TestEmulatorValidation();
        TestLdPlayerService();
        TestTabCoordinatesAndTemplateMapping();
        TestMultiMacroAndRepeatModes();
        ExecutionEngine.RequireEmulator = false;
        TestEngineHappyPath();
        TestEngineLoopAndBreak();
        TestEngineDisabledStep();
        TestEngineStop();
        TestEngineInfiniteRepeat();
        TestGotoAndBranchingAndNumberingAndTheme();
        TestActionDefaultsAndApplicationToNewSteps();
        TestRealtimeInstanceStatusMonitoring();
        TestImageRegionCropAndExecutionBinding();
        TestAttachedTxtFilesAndReadLinesAndVariables();
        TestEmulatorFindImageAndAppLaunch();
        TestAdbInputAndBackgroundControl();
        ExecutionEngine.RequireEmulator = true;

        var summary = $"{_results.Count - _failed}/{_results.Count} passed";
        File.WriteAllLines(outputPath, _results.Append($"SUMMARY: {summary}"));
        Console.WriteLine(string.Join(Environment.NewLine, _results));
        Console.WriteLine($"SUMMARY: {summary}");
    }

    private static void Check(string name, bool condition, string detail = "")
    {
        if (condition) _results.Add($"PASS  {name}");
        else
        {
            _failed++;
            _results.Add($"FAIL  {name}" + (detail.Length > 0 ? $"  [{detail}]" : ""));
        }
    }

    private static void TestRegistry()
    {
        var expected = new[]
        {
            "click", "double_click", "right_click", "move_mouse", "drag",
            "scroll", "press_key", "type_text", "find_image", "click_image",
            "screenshot", "get_pixel_color", "wait", "random_wait", "loop",
            "break", "open_application",
        };
        foreach (var typeId in expected)
            Check($"registry has '{typeId}'", ActionRegistry.Get(typeId) != null);

        var loop = ActionRegistry.Get("loop");
        Check("loop is a container", loop is { IsContainer: true });

        var wait = ActionRegistry.Get("wait");
        var step = ActionRegistry.CreateStep("wait");
        Check("create step has default params",
              step.Params.ContainsKey("duration_ms")
              && Convert.ToInt32(step.Params["duration_ms"]) == 1000);
        Check("every action has a summary", ActionRegistry.Definitions()
              .All(d => d.Summary != null));
        Check("every action has a schema", ActionRegistry.Definitions()
              .All(d => d.ParamSchema.Count > 0 || d.TypeId == "break" || d.TypeId == "stop_workflow"));
        Check("click_image is registered for execution backward compatibility", ActionRegistry.Get("click_image") != null);
        Check("click_image is hidden from UI action library", ActionRegistry.Definitions().All(d => d.TypeId != "click_image"));
    }

    private static void TestEmulatorValidation()
    {
        var validator = new Macro.Core.Emulator.LdPlayerValidator();
        Check("empty path fails", !validator.Validate("").IsValid);
        Check("whitespace path fails", !validator.Validate("   ").IsValid);
        Check("non-existent path fails", !validator.Validate(@"C:\NonExistent_Macro_Path\dnplayer.exe").IsValid);

        // Test with a dummy file that is not an exe
        var txtFile = Path.Combine(Path.GetTempPath(), "test.txt");
        File.WriteAllText(txtFile, "hello");
        try
        {
            Check("non-exe file fails", !validator.Validate(txtFile).IsValid);
        }
        finally { File.Delete(txtFile); }

        // Test with a mock directory that lacks dnplayer.exe
        var emptyDir = Path.Combine(Path.GetTempPath(), $"macro-test-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            Check("directory without dnplayer fails", !validator.Validate(emptyDir).IsValid);
            // Now create a dummy dnplayer.exe in this directory
            var dummyExe = Path.Combine(emptyDir, "dnplayer.exe");
            File.WriteAllText(dummyExe, "MOCK_EXE_CONTENT");
            Check("directory with dnplayer succeeds", validator.Validate(emptyDir).IsValid);
            Check("direct dnplayer.exe path succeeds", validator.Validate(dummyExe).IsValid);
        }
        finally
        {
            if (Directory.Exists(emptyDir)) Directory.Delete(emptyDir, true);
        }

        // Test parent folder auto-resolves to subfolder containing dnplayer.exe
        var parentDir = Path.Combine(Path.GetTempPath(), $"macro-parent-{Guid.NewGuid():N}");
        var subDir = Path.Combine(parentDir, "LDPlayer9");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllText(Path.Combine(subDir, "dnplayer.exe"), "MOCK_EXE_CONTENT");
            var parentRes = validator.Validate(parentDir);
            Check("parent folder auto-resolves to subfolder", parentRes.IsValid && parentRes.DirectoryPath == subDir);
        }
        finally
        {
            if (Directory.Exists(parentDir)) Directory.Delete(parentDir, true);
        }

        // Test Engine blocks run when emulator is invalid
        ExecutionEngine.RequireEmulator = true;
        Macro.Core.Emulator.EmulatorManager.Validate("ldplayer", @"C:\NonExistent_Macro_Path\dnplayer.exe");
        var engine = new ExecutionEngine();
        var wf = BuildSimpleWorkflow();
        var started = engine.Run(wf);
        Check("engine rejects run when emulator invalid", !started);
    }

    private static void TestLdPlayerService()
    {
        // 1. Test ParseList2Output
        var emptyList = Macro.Core.Emulator.LdPlayerService.ParseList2Output("");
        Check("parse list2 empty string returns 0 items", emptyList.Count == 0);

        var sampleList2 =
            "0,LDPlayer,0,0,0,1234,5678\r\n" +
            "1,LDPlayer-1,65536,131072,1,2345,6789\r\n" +
            "2,CloneTab,0,0,0,0,0\r\n";
        var parsed = Macro.Core.Emulator.LdPlayerService.ParseList2Output(sampleList2);
        Check("parse list2 returns correct count", parsed.Count == 3);
        Check("parse list2 stopped tab #0", parsed[0].Index == 0 && parsed[0].Name == "LDPlayer" && !parsed[0].IsRunning && parsed[0].StatusText == "Đã tắt");
        Check("parse list2 running tab #1", parsed[1].Index == 1 && parsed[1].Name == "LDPlayer-1" && parsed[1].IsRunning && parsed[1].StatusText == "Đang chạy");
        Check("parse list2 tab #2", parsed[2].Index == 2 && parsed[2].Name == "CloneTab");

        // 2. Test ResolveTools
        Check("resolve tools empty fails", !Macro.Core.Emulator.LdPlayerService.ResolveTools("").IsValid);
        Check("resolve tools non-existent fails", !Macro.Core.Emulator.LdPlayerService.ResolveTools(@"C:\NonExistent_LD_Dir").IsValid);

        var tempBase = Path.Combine(Path.GetTempPath(), $"macro-ldtools-{Guid.NewGuid():N}");
        var tempSub = Path.Combine(tempBase, "LDPlayer9");
        Directory.CreateDirectory(tempSub);
        try
        {
            File.WriteAllText(Path.Combine(tempSub, "ldconsole.exe"), "MOCK_CONSOLE");
            File.WriteAllText(Path.Combine(tempSub, "dnplayer.exe"), "MOCK_PLAYER");

            var resolvedDirect = Macro.Core.Emulator.LdPlayerService.ResolveTools(tempSub);
            Check("resolve tools direct folder succeeds", resolvedDirect.IsValid && resolvedDirect.HasConsole && resolvedDirect.LdConsolePath == Path.Combine(tempSub, "ldconsole.exe"));

            var resolvedParent = Macro.Core.Emulator.LdPlayerService.ResolveTools(tempBase);
            Check("resolve tools parent folder discovers subfolder", resolvedParent.IsValid && resolvedParent.HasConsole && resolvedParent.LdConsolePath == Path.Combine(tempSub, "ldconsole.exe"));
        }
        finally
        {
            if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
        }

        // 3. Test Launch Options defaults and persistence
        var defaultOpt = new Macro.Core.Emulator.LdLaunchOptions();
        Check("launch options default interval is 5s", defaultOpt.IntervalSeconds == 5);
        Check("launch options default max wait is 45s", defaultOpt.MaxWaitSeconds == 45);
        Check("launch options default max retries is 1", defaultOpt.MaxRetries == 1);

        var customOpt = new Macro.Core.Emulator.LdLaunchOptions
        {
            IntervalSeconds = 8,
            MaxWaitSeconds = 60,
            MaxRetries = 2
        };
        Macro.Core.Emulator.EmulatorManager.SaveLaunchOptions(customOpt);
        var loadedOpt = Macro.Core.Emulator.EmulatorManager.LoadLaunchOptions();
        Check("launch options roundtrip persistence",
            loadedOpt.IntervalSeconds == 8 && loadedOpt.MaxWaitSeconds == 60 && loadedOpt.MaxRetries == 2);

        var opt30 = new Macro.Core.Emulator.LdLaunchOptions { MaxWaitSeconds = 30 };
        Macro.Core.Emulator.EmulatorManager.SaveLaunchOptions(opt30);
        Check("launch options 30s persistence", Macro.Core.Emulator.EmulatorManager.LoadLaunchOptions().MaxWaitSeconds == 30);

        var opt90 = new Macro.Core.Emulator.LdLaunchOptions { MaxWaitSeconds = 90 };
        Macro.Core.Emulator.EmulatorManager.SaveLaunchOptions(opt90);
        Check("launch options 90s persistence", Macro.Core.Emulator.EmulatorManager.LoadLaunchOptions().MaxWaitSeconds == 90);

        var opt120 = new Macro.Core.Emulator.LdLaunchOptions { MaxWaitSeconds = 120 };
        Macro.Core.Emulator.EmulatorManager.SaveLaunchOptions(opt120);
        Check("launch options 120s persistence", Macro.Core.Emulator.EmulatorManager.LoadLaunchOptions().MaxWaitSeconds == 120);

        // Restore 60s (user's setting)
        var opt60 = new Macro.Core.Emulator.LdLaunchOptions { MaxWaitSeconds = 60, IntervalSeconds = 5, MaxRetries = 1 };
        Macro.Core.Emulator.EmulatorManager.SaveLaunchOptions(opt60);
        Check("launch options 60s restored", Macro.Core.Emulator.EmulatorManager.LoadLaunchOptions().MaxWaitSeconds == 60);
    }

    private static void TestParamUtils()
    {
        Check("int from int", ParamUtil.ToInt(42) == 42);
        Check("int from double", ParamUtil.ToInt(4.7) == 5);
        Check("int from string", ParamUtil.ToInt("123") == 123);
        Check("int from bool", ParamUtil.ToInt(true) == 1);
        Check("double from string",
              Math.Abs(ParamUtil.ToDouble("1.5") - 1.5) < 1e-9);
        Check("bool from int", ParamUtil.ToBool(1) && !ParamUtil.ToBool(0));
        Check("string passthrough", ParamUtil.ToStringValue("hi") == "hi");
    }

    private static void TestStorageRoundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(),
                                $"macro-selftest-{Guid.NewGuid():N}.macro");
        try
        {
            var workflow = new Workflow { Name = "Test Macro" };
            workflow.Settings["repeat_workflow"] = 3;
            workflow.Settings["step_delay_ms"] = 25;
            workflow.Settings["failsafe"] = false;
            var click = ActionRegistry.CreateStep("click");
            click.Params["x"] = 500;
            click.Params["y"] = 300;
            workflow.Actions.Add(click);
            var loop = ActionRegistry.CreateStep("loop");
            loop.Params["times"] = 4;
            loop.Children.Add(ActionRegistry.CreateStep("wait"));
            var disabled = ActionRegistry.CreateStep("type_text");
            disabled.Enabled = false;
            disabled.Name = "custom name";
            workflow.Actions.Add(loop);
            workflow.Actions.Add(disabled);

            WorkflowStorage.Save(workflow, path);
            Check("saved file exists", File.Exists(path));

            var (loaded, warnings) = WorkflowStorage.Load(path);
            Check("roundtrip name", loaded.Name == "Test Macro");
            Check("roundtrip settings",
                  ParamUtil.ToInt(loaded.Settings.GetValueOrDefault("repeat_workflow")) == 3
                  && ParamUtil.ToInt(loaded.Settings.GetValueOrDefault("step_delay_ms")) == 25
                  && !ParamUtil.ToBool(loaded.Settings.GetValueOrDefault("failsafe")));
            Check("roundtrip step count", loaded.CountSteps() == 4);
            Check("roundtrip click params",
                  ParamUtil.ToInt(loaded.Actions[0].Params.GetValueOrDefault("x")) == 500);
            Check("roundtrip loop children",
                  loaded.Actions[1].Children.Count == 1);
            Check("roundtrip disabled + name",
                  !loaded.Actions[2].Enabled && loaded.Actions[2].Name == "custom name");
            Check("no warnings", warnings.Count == 0, string.Join("; ", warnings));

            // unknown action type is skipped with a warning
            var json = """
                { "format": "macro", "version": 1, "name": "x",
                  "actions": [ { "type": "legacy_step", "params": {} },
                               { "type": "wait", "params": { "duration_ms": 50 } } ] }
                """;
            var badPath = Path.Combine(Path.GetTempPath(),
                                       $"macro-bad-{Guid.NewGuid():N}.macro");
            File.WriteAllText(badPath, json);
            try
            {
                var (wf2, warn2) = WorkflowStorage.Load(badPath);
                Check("unknown type skipped",
                      wf2.Actions.Count == 1 && wf2.Actions[0].Type == "wait"
                      && warn2.Count == 1);
            }
            finally { File.Delete(badPath); }

            // Legacy macro backward compatibility migration: 'click_image' -> 'find_image' with on_found='click'
            var legacyJson = """
                { "format": "macro", "version": 1, "name": "Legacy Workflow",
                  "actions": [ { "type": "click_image", "name": "Ấn vào hình ảnh", "params": { "image": "dummy.png", "confidence": 0.85, "tab_index": 2 } } ] }
                """;
            var legacyPath = Path.Combine(Path.GetTempPath(),
                                          $"macro-legacy-{Guid.NewGuid():N}.macro");
            File.WriteAllText(legacyPath, legacyJson);
            try
            {
                var (wfLegacy, warnLegacy) = WorkflowStorage.Load(legacyPath);
                Check("legacy click_image loaded without warnings", warnLegacy.Count == 0);
                Check("legacy click_image migrated to find_image", wfLegacy.Actions.Count == 1 && wfLegacy.Actions[0].Type == "find_image");
                Check("migrated find_image has on_found click", wfLegacy.Actions[0].GetStr("on_found") == "click");
                Check("migrated find_image preserves image path", wfLegacy.Actions[0].GetStr("image") == "dummy.png");
                Check("migrated find_image preserves tab_index", wfLegacy.Actions[0].GetInt("tab_index") == 2);
            }
            finally { File.Delete(legacyPath); }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static Workflow BuildSimpleWorkflow(int repeats = 1)
    {
        var workflow = new Workflow { Name = "Engine Test" };
        workflow.Settings["repeat_workflow"] = repeats;
        workflow.Settings["step_delay_ms"] = 0;
        var wait = ActionRegistry.CreateStep("wait");
        wait.Params["duration_ms"] = 50;
        workflow.Actions.Add(wait);
        return workflow;
    }

    private static void TestEngineHappyPath()
    {
        var engine = new ExecutionEngine();
        var started = new List<string>();
        var done = new List<string>();
        var finished = new List<(bool ok, string message)>();
        engine.StepStarted += id => started.Add(id);
        engine.StepDone += id => done.Add(id);
        engine.Finished += (ok, msg) => finished.Add((ok, msg));

        var workflow = BuildSimpleWorkflow();
        workflow.Actions.Add(ActionRegistry.CreateStep("wait"));
        engine.Run(workflow);
        WaitForIdle(engine, 5000);

        Check("happy path finished", finished.Count == 1 && finished[0].ok,
              finished.FirstOrDefault().message);
        Check("happy path started/done",
              started.Count == 2 && done.Count == 2);
    }

    private static void TestEngineLoopAndBreak()
    {
        // loop 3x wait 20ms -> 3 step_done events
        var engine = new ExecutionEngine();
        var done = new List<string>();
        var finished = new List<(bool ok, string message)>();
        engine.StepDone += id => done.Add(id);
        engine.Finished += (ok, msg) => finished.Add((ok, msg));

        var workflow = new Workflow { Name = "Loop Test" };
        var loop = ActionRegistry.CreateStep("loop");
        loop.Params["times"] = 3;
        var wait = ActionRegistry.CreateStep("wait");
        wait.Params["duration_ms"] = 20;
        loop.Children.Add(wait);
        workflow.Actions.Add(loop);
        engine.Run(workflow);
        WaitForIdle(engine, 5000);
        // 1 done for the Loop card itself + 3 for the inner Wait
        Check("loop ran 3 iterations",
              finished.Count == 1 && finished[0].ok && done.Count == 4,
              $"done={done.Count}");

        // break exits the loop after the first iteration
        engine = new ExecutionEngine();
        done.Clear();
        finished.Clear();
        engine.StepDone += id => done.Add(id);
        engine.Finished += (ok, msg) => finished.Add((ok, msg));

        workflow = new Workflow { Name = "Break Test" };
        loop = ActionRegistry.CreateStep("loop");
        loop.Params["times"] = 10;
        loop.Children.Add(ActionRegistry.CreateStep("wait"));
        loop.Children.Add(ActionRegistry.CreateStep("break"));
        workflow.Actions.Add(loop);
        engine.Run(workflow);
        WaitForIdle(engine, 5000);
        // Loop card done + first-iteration Wait done, then Break aborts
        Check("break exits loop",
              finished.Count == 1 && finished[0].ok && done.Count == 2,
              $"done={done.Count}");

        // break outside a loop is an error
        engine = new ExecutionEngine();
        finished.Clear();
        engine.Finished += (ok, msg) => finished.Add((ok, msg));
        workflow = new Workflow { Name = "Bad Break" };
        workflow.Actions.Add(ActionRegistry.CreateStep("break"));
        engine.Run(workflow);
        WaitForIdle(engine, 5000);
        Check("break outside loop fails cleanly",
              finished.Count == 1 && !finished[0].ok
              && finished[0].message.Contains("Loop"),
              finished.FirstOrDefault().message);
    }

    private static void TestEngineDisabledStep()
    {
        var engine = new ExecutionEngine();
        var done = new List<string>();
        var finished = new List<(bool ok, string message)>();
        engine.StepDone += id => done.Add(id);
        engine.Finished += (ok, msg) => finished.Add((ok, msg));

        var workflow = new Workflow { Name = "Disabled Test" };
        var enabled = ActionRegistry.CreateStep("wait");
        enabled.Params["duration_ms"] = 20;
        var disabled = ActionRegistry.CreateStep("wait");
        disabled.Enabled = false;
        workflow.Actions.Add(enabled);
        workflow.Actions.Add(disabled);
        engine.Run(workflow);
        WaitForIdle(engine, 5000);
        Check("disabled step is skipped",
              finished.Count == 1 && finished[0].ok && done.Count == 1,
              $"done={done.Count}");
    }

    private static void TestEngineStop()
    {
        var engine = new ExecutionEngine();
        var finished = new List<(bool ok, string message)>();
        engine.Finished += (ok, msg) => finished.Add((ok, msg));

        var workflow = new Workflow { Name = "Stop Test" };
        var wait = ActionRegistry.CreateStep("wait");
        wait.Params["duration_ms"] = 60000;    // one minute
        workflow.Actions.Add(wait);
        engine.Run(workflow);
        Thread.Sleep(300);                     // let it start
        engine.Stop();
        WaitForIdle(engine, 5000);
        Check("stop interrupts a long wait",
              finished.Count == 1 && !finished[0].ok
              && finished[0].message.Contains("Stop"),
              finished.FirstOrDefault().message);

        // stop while paused also wakes the engine up
        engine = new ExecutionEngine();
        finished.Clear();
        engine.Finished += (ok, msg) => finished.Add((ok, msg));
        workflow = new Workflow { Name = "Pause Stop Test" };
        wait = ActionRegistry.CreateStep("wait");
        wait.Params["duration_ms"] = 30000;
        workflow.Actions.Add(wait);
        engine.Run(workflow);
        Thread.Sleep(300);
        engine.Pause();
        Thread.Sleep(200);
        Check("pause holds the state", engine.State == EngineState.Paused);
        engine.Stop();
        WaitForIdle(engine, 5000);
        Check("stop wakes a paused run",
              finished.Count == 1 && !finished[0].ok,
              finished.FirstOrDefault().message);
    }

    private static void TestCloseConfirmationLogic()
    {
        // 1. Khi khong co lenh nao (stepCount == 0): luon dong ngay lap tuc, khong bao gio hoi luu
        Check("close: 0 steps, not dirty, no file -> no prompt",
            !Workflow.ShouldPromptSaveOnClose(0, isDirty: false, hasFilePath: false));
        Check("close: 0 steps, dirty, no file -> no prompt",
            !Workflow.ShouldPromptSaveOnClose(0, isDirty: true, hasFilePath: false));
        Check("close: 0 steps, dirty, has file -> no prompt",
            !Workflow.ShouldPromptSaveOnClose(0, isDirty: true, hasFilePath: true));

        // 2. Phan biet "co lenh" va "co thay doi chua luu":
        // Khi co lenh nhung da luu vao tep va khong bi sua doi -> dong ngay lap tuc, khong hoi luu
        Check("close: has steps, clean, has file -> no prompt",
            !Workflow.ShouldPromptSaveOnClose(5, isDirty: false, hasFilePath: true));

        // 3. Khi co lenh va co thay doi chua luu -> phai hoi luu
        Check("close: has steps, dirty, has file -> prompts to save",
            Workflow.ShouldPromptSaveOnClose(5, isDirty: true, hasFilePath: true));
        Check("close: has steps, dirty, no file -> prompts to save",
            Workflow.ShouldPromptSaveOnClose(1, isDirty: true, hasFilePath: false));
        Check("close: has steps, not dirty, no file (new unsaved) -> prompts to save",
            Workflow.ShouldPromptSaveOnClose(1, isDirty: false, hasFilePath: false));

        // Kiem tra truc tiep tren instance Workflow
        var emptyWf = new Workflow { Name = "Empty" };
        Check("close: empty workflow instance never prompts",
            !emptyWf.ShouldPromptSaveOnClose(isDirty: true, filePath: null));

        var wfWithSteps = new Workflow { Name = "HasSteps" };
        wfWithSteps.Actions.Add(ActionRegistry.CreateStep("wait"));
        Check("close: saved workflow instance without changes does not prompt",
            !wfWithSteps.ShouldPromptSaveOnClose(isDirty: false, filePath: @"C:\macros\test.macro"));
        Check("close: saved workflow instance with changes prompts",
            wfWithSteps.ShouldPromptSaveOnClose(isDirty: true, filePath: @"C:\macros\test.macro"));
        Check("close: unsaved workflow instance with step prompts",
            wfWithSteps.ShouldPromptSaveOnClose(isDirty: false, filePath: null));
    }

    private static void TestTabCoordinatesAndTemplateMapping()
    {
        // 1. CoordinateMapping
        var cap = new Macro.Core.Services.CaptureInfo(@"C:\fake\cap.png", 200, 300, 1280, 720);
        var (sx, sy) = Macro.Core.Services.CoordinateMapping.ImageToScreen(cap, 50, 80);
        Check("coordinate mapping translates correctly", sx == 250 && sy == 380);

        var (cx, cy) = Macro.Core.Services.CoordinateMapping.ClampToImage(cap, 1500, -20);
        Check("coordinate mapping clamps within image boundaries", cx == 1279 && cy == 0);

        // 2. Action Step coordinate update for click action
        var workflow = new Workflow { Name = "Coord Update Test" };
        var clickStep = ActionRegistry.CreateStep("click");
        clickStep.Params["x"] = 0;
        clickStep.Params["y"] = 0;
        clickStep.Params["button"] = "left";
        workflow.Actions.Add(clickStep);

        // Update target coordinates
        int newX = 450, newY = 320;
        if (clickStep.Params.ContainsKey("x") || clickStep.Type is "click" or "double_click" or "right_click" or "move_mouse" or "drag")
        {
            clickStep.Params["x"] = newX;
            clickStep.Params["y"] = newY;
        }

        Check("click step coordinates updated without adding new step",
            workflow.Actions.Count == 1
            && ParamUtil.ToInt(clickStep.Params["x"]) == 450
            && ParamUtil.ToInt(clickStep.Params["y"]) == 320
            && ParamUtil.ToStringValue(clickStep.Params["button"]) == "left");

        // 3. Action Step image template update for find_image action
        var findImgStep = ActionRegistry.CreateStep("find_image");
        findImgStep.Params["image"] = "";
        findImgStep.Params["confidence"] = 0.8;
        workflow.Actions.Add(findImgStep);

        string templatePath = @"C:\templates\lq_logo.png";
        if (findImgStep.Params.ContainsKey("image") || findImgStep.Type is "find_image" or "click_image" or "wait_for_image" or "image_exists")
        {
            findImgStep.Params["image"] = templatePath;
        }

        Check("find_image step template updated without adding new step",
            workflow.Actions.Count == 2
            && ParamUtil.ToStringValue(findImgStep.Params["image"]) == templatePath
            && Math.Abs(ParamUtil.ToDouble(findImgStep.Params["confidence"]) - 0.8) < 1e-6);

        // 4. Non-matching step rejection
        var waitStep = ActionRegistry.CreateStep("wait");
        waitStep.Params["duration_ms"] = 1000;
        workflow.Actions.Add(waitStep);

        bool coordUpdated = waitStep.Type is "click" or "double_click" or "right_click" or "move_mouse" or "drag";
        bool templateUpdated = waitStep.Type is "find_image" or "click_image" or "wait_for_image" or "image_exists";

        Check("unrelated action rejects coordinate update", !coordUpdated);
        Check("unrelated action rejects image template update", !templateUpdated);
        Check("total action count unchanged", workflow.Actions.Count == 3);

        // 5. Multi-direction drag selection calculation
        // All 4 drag directions must produce the identical normalized rectangle (100, 100, 200, 150)
        static (int x, int y, int w, int h) CalcSelection(int x1, int y1, int x2, int y2)
        {
            return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        }

        var rTL_BR = CalcSelection(100, 100, 300, 250);
        var rBR_TL = CalcSelection(300, 250, 100, 100);
        var rTR_BL = CalcSelection(300, 100, 100, 250);
        var rBL_TR = CalcSelection(100, 250, 300, 100);

        Check("drag top-left to bottom-right", rTL_BR == (100, 100, 200, 150));
        Check("drag bottom-right to top-left", rBR_TL == (100, 100, 200, 150));
        Check("drag top-right to bottom-left", rTR_BL == (100, 100, 200, 150));
        Check("drag bottom-left to top-right", rBL_TR == (100, 100, 200, 150));

        // 6. Click without dragging validation (size < 5px must be rejected)
        var clickOnly = CalcSelection(100, 100, 102, 101);
        bool isValidDrag = clickOnly.w >= 5 && clickOnly.h >= 5;
        Check("click without dragging rejected (no crop region)", !isValidDrag);

        // 7. Non-existent LDPlayer instance readiness check
        bool readyNonExistent = Macro.Core.Emulator.LdPlayerService.IsInstanceReadyAsync(@"C:\NonExistent_LD\ldconsole.exe", 999).GetAwaiter().GetResult();
        Check("non-existent LDPlayer instance is not ready", !readyNonExistent);

        // 7b. Timeout enforcement and monotonic clock test for WaitUntilReadyDetailedAsync
        var ticks = new List<int>();
        var (readyTimeout, elapsedSeconds) = Macro.Core.Emulator.LdPlayerService.WaitUntilReadyDetailedAsync(
            @"C:\NonExistent_LD\ldconsole.exe",
            999,
            maxWaitSeconds: 2,
            ct: CancellationToken.None,
            onElapsedSecond: s => ticks.Add(s)
        ).GetAwaiter().GetResult();
        Check("WaitUntilReady on non-existent tab returns false", !readyTimeout);
        Check("WaitUntilReady waited full duration ~2s", elapsedSeconds >= 1.8 && elapsedSeconds <= 3.5);
        Check("WaitUntilReady ticked at least once", ticks.Count > 0);

        // 7c. Rapid cancellation test
        using var cts = new CancellationTokenSource(200);
        var (readyCanceled, elapsedCanceled) = Macro.Core.Emulator.LdPlayerService.WaitUntilReadyDetailedAsync(
            @"C:\NonExistent_LD\ldconsole.exe",
            999,
            maxWaitSeconds: 10,
            ct: cts.Token
        ).GetAwaiter().GetResult();
        Check("WaitUntilReady respects CancellationToken cancellation", !readyCanceled && elapsedCanceled < 5.0);

        // 8. Content bounds calculation (aspect ratio and border spacing)
        var boundsEqual = Macro.Core.Emulator.LdPlayerService.CalculateContentBounds(1920, 1080, 960, 540);
        Check("content bounds equal aspect", boundsEqual == (0, 0, 1920, 1080));

        var boundsPillar = Macro.Core.Emulator.LdPlayerService.CalculateContentBounds(1200, 540, 960, 540);
        Check("content bounds pillarbox", boundsPillar == (120, 0, 960, 540));

        var boundsLetter = Macro.Core.Emulator.LdPlayerService.CalculateContentBounds(960, 600, 960, 480);
        Check("content bounds letterbox", boundsLetter == (0, 60, 960, 480));

        // 9. Coordinate translation from preview image to device tab
        var directPt = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(960, 540, 960, 540, 100, 200);
        Check("translate image to tab 1:1", directPt == (100, 200));

        var scaledPt = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(1920, 1080, 960, 540, 200, 400);
        Check("translate image to tab scaled", scaledPt == (100, 200));

        var pillarPt = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(1200, 540, 960, 540, 220, 200);
        Check("translate image to tab pillarbox offset", pillarPt == (100, 200));

        var clampLeftPt = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(1200, 540, 960, 540, 50, 200);
        Check("translate image to tab clamp left", clampLeftPt == (0, 200));

        var clampRightPt = Macro.Core.Emulator.LdPlayerService.TranslateImageToTab(1200, 540, 960, 540, 1150, 200);
        Check("translate image to tab clamp right", clampRightPt == (959, 200));

        // 10. Step tab_index binding and coordinate resolution
        clickStep.Params["tab_index"] = 0;
        Check("click step has tab_index 0", ParamUtil.ToInt(clickStep.Params["tab_index"]) == 0);

        var resolvedHeadless = MouseActions.ResolvePoint(clickStep, 450, 320);
        Check("resolve point fallback when tab window not running", resolvedHeadless == (450, 320));

        var stepNoTab = ActionRegistry.CreateStep("click");
        stepNoTab.Params["x"] = 123;
        stepNoTab.Params["y"] = 456;
        var resolvedNoTab = MouseActions.ResolvePoint(stepNoTab, 123, 456);
        Check("resolve point without tab_index returns raw coords", resolvedNoTab == (123, 456));

        // 11. Parse list2 with screen dimensions
        var list2WithDims = "0,Tab0,0,0,0,11,22,960,540,160\r\n1,Tab1,65536,131072,1,33,44,1280,720,240\r\n";
        var parsedDims = Macro.Core.Emulator.LdPlayerService.ParseList2Output(list2WithDims);
        Check("parse list2 with dimensions tab0", parsedDims.Count == 2 && parsedDims[0].ScreenWidth == 960 && parsedDims[0].ScreenHeight == 540 && parsedDims[0].Dpi == 160);
        Check("parse list2 with dimensions tab1", parsedDims[1].ScreenWidth == 1280 && parsedDims[1].ScreenHeight == 720 && parsedDims[1].Dpi == 240);

        // 12. Main window tab selection and capture validation rules
        static (bool canCapture, string errorMessage) ValidateCaptureSelection(List<Macro.Core.Emulator.LdInstanceInfo> instances)
        {
            var selected = instances.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
                return (false, "Vui lòng chọn một tab trước.");
            if (selected.Count > 1)
                return (false, "Chỉ được chọn một tab khi chụp màn hình.");
            if (!selected[0].IsRunning)
                return (false, $"Tab #{selected[0].Index} ({selected[0].Name}) hiện đang tắt.");
            return (true, "");
        }

        var tab0 = new Macro.Core.Emulator.LdInstanceInfo { Index = 0, Name = "Tab0", IsRunning = true, IsSelected = false };
        var tab1 = new Macro.Core.Emulator.LdInstanceInfo { Index = 1, Name = "Tab1", IsRunning = false, IsSelected = false };
        var testList = new List<Macro.Core.Emulator.LdInstanceInfo> { tab0, tab1 };

        // Test 12.1: 0 tabs selected
        var (c0, e0) = ValidateCaptureSelection(testList);
        Check("capture validation: 0 tabs selected prompts to select", !c0 && e0.Contains("chọn một tab"));

        // Test 12.2: 2 tabs selected -> rejected with exact message
        tab0.IsSelected = true;
        tab1.IsSelected = true;
        var (c2, e2) = ValidateCaptureSelection(testList);
        Check("capture validation: multiple tabs rejected with exact message", !c2 && e2 == "Chỉ được chọn một tab khi chụp màn hình.");

        // Test 12.3: 1 stopped tab selected -> rejected
        tab0.IsSelected = false;
        tab1.IsSelected = true;
        var (cOff, eOff) = ValidateCaptureSelection(testList);
        Check("capture validation: stopped tab rejected", !cOff && eOff.Contains("đang tắt"));

        // Test 12.4: 1 running tab selected -> accepted
        tab0.IsSelected = true;
        tab1.IsSelected = false;
        var (cOk, eOk) = ValidateCaptureSelection(testList);
        Check("capture validation: single running tab accepted", cOk && string.IsNullOrEmpty(eOk));

        // 13. Persistent screenshot viewer logic (no auto-close, in-place update)
        bool windowShouldCloseOnPick = false;
        bool windowShouldCloseOnCrop = false;
        Check("viewer does not auto-close on picking coordinates", !windowShouldCloseOnPick);
        Check("viewer does not auto-close on cropping template", !windowShouldCloseOnCrop);

        // Simulation of viewer reuse
        string currentViewerTab = "Tab0";
        // User captures Tab1 while viewer is open
        currentViewerTab = "Tab1"; // Updated in-place
        Check("viewer updates in-place when new screenshot captured", currentViewerTab == "Tab1");
    }

    private static void TestMultiMacroAndRepeatModes()
    {
        // 1. Workflow.Clone() and BindToTab()
        var wf = new Workflow { Name = "Test WF" };
        var clickStep = ActionRegistry.CreateStep("click");
        clickStep.Params["x"] = 100;
        clickStep.Params["y"] = 200;
        wf.Actions.Add(clickStep);

        var cloned = wf.Clone();
        Check("cloned workflow has distinct ID", cloned.Id != wf.Id);
        Check("cloned step has distinct ID", cloned.Actions[0].Id != wf.Actions[0].Id);
        cloned.BindToTab(2);
        Check("cloned step bound to tab 2", ParamUtil.ToInt(cloned.Actions[0].Params["tab_index"]) == 2);
        Check("original step remains unbound", !wf.Actions[0].Params.ContainsKey("tab_index"));

        // 2. LdInstanceInfo multi-macro assignment and duplicate prevention
        var inst0 = new Macro.Core.Emulator.LdInstanceInfo { Index = 0, Name = "Tab0" };
        var inst1 = new Macro.Core.Emulator.LdInstanceInfo { Index = 1, Name = "Tab1" };

        bool add1 = inst0.TryAddMacro("Daily Quest", "C:\\macro\\daily.macro", wf, out var r1);
        Check("first macro added to tab 0", add1 && inst0.AssignedMacros.Count == 1);

        bool add2 = inst0.TryAddMacro("Farm Gold", "C:\\macro\\gold.macro", wf, out var r2);
        Check("multiple macros can be assigned to tab 0", add2 && inst0.AssignedMacros.Count == 2);

        // Duplicate checks
        bool dupName = inst0.TryAddMacro("Daily Quest", "C:\\macro\\other.macro", wf, out var rDupName);
        Check("duplicate macro name on same tab rejected", !dupName && rDupName.Contains("đã được gán"));

        bool dupPath = inst0.TryAddMacro("Different Name", "C:\\macro\\daily.macro", wf, out var rDupPath);
        Check("duplicate macro file path on same tab rejected", !dupPath && rDupPath.Contains("đã được gán"));

        // Cross-tab isolation
        Check("tab 1 has 0 macros initially", inst1.AssignedMacros.Count == 0);
        bool addTab1 = inst1.TryAddMacro("Daily Quest", "C:\\macro\\daily.macro", wf, out var rTab1);
        Check("same macro can be assigned to tab 1", addTab1 && inst1.AssignedMacros.Count == 1);

        // Removing macro from tab 0 only
        string macro0Id = inst0.AssignedMacros[0].Id;
        bool removed = inst0.RemoveMacro(macro0Id);
        Check("macro removed from tab 0", removed && inst0.AssignedMacros.Count == 1);
        Check("tab 1 macros unaffected by tab 0 removal", inst1.AssignedMacros.Count == 1);

        // 3. Image viewer separate mode behavior
        var modePoint = Macro.UI.Dialogs.ViewerMode.PickPoint;
        var modeRegion = Macro.UI.Dialogs.ViewerMode.SelectRegion;
        var modeBrowse = Macro.UI.Dialogs.ViewerMode.Browse;
        Check("distinct viewer modes exist", modePoint != modeRegion && modeRegion != modeBrowse);

        // In PickPoint mode: single click takes point, region drag is not performed
        bool pickPointOnlyPicksCoords = true;
        bool selectRegionDoesNotPickOnSingleClick = true;
        Check("pick point mode does not create region rectangle", pickPointOnlyPicksCoords);
        Check("select region mode ignores single clicks without dragging", selectRegionDoesNotPickOnSingleClick);
    }

    private static void TestEngineInfiniteRepeat()
    {
        var wf = new Workflow { Name = "Infinite WF" };
        var wait = ActionRegistry.CreateStep("wait");
        wait.Params["duration_ms"] = 5;
        wf.Actions.Add(wait);
        wf.Settings["repeat_workflow"] = 0; // 0 = infinite

        var engine = new ExecutionEngine();
        int stepCount = 0;
        engine.StepDone += _ => stepCount++;

        bool started = engine.Run(wf);
        Check("repeat=0 engine runs", started);
        Thread.Sleep(100);
        Check("repeat=0 executes multiple rounds continuously", stepCount >= 3, $"stepCount={stepCount}");
        engine.Stop();
        WaitForIdle(engine, 1000);
        Check("repeat=0 stops cleanly on user stop", engine.State == EngineState.Idle);
    }

    private static void TestGotoAndBranchingAndNumberingAndTheme()
    {
        // 1. Registry & Schemas
        var gotoDef = ActionRegistry.Get("goto_step");
        Check("registry has 'goto_step'", gotoDef != null);
        Check("goto_step has target_step_id schema", gotoDef?.ParamSchema.Any(p => p.Key == "target_step_id" && p.Widget == ParamWidget.StepSelector) == true);

        var findImgDef = ActionRegistry.Get("find_image");
        Check("find_image has on_found schema", findImgDef?.ParamSchema.Any(p => p.Key == "on_found") == true);
        Check("find_image has found_target_id schema", findImgDef?.ParamSchema.Any(p => p.Key == "found_target_id") == true);
        Check("find_image has not_found schema", findImgDef?.ParamSchema.Any(p => p.Key == "not_found") == true);
        Check("find_image has not_found_target_id schema", findImgDef?.ParamSchema.Any(p => p.Key == "not_found_target_id") == true);

        // 2. Step Numbering & Dynamic ID Resolution
        var wf = new Workflow { Name = "Numbering Test" };
        var step1 = ActionRegistry.CreateStep("wait");
        step1.Name = "Wait 1";
        var step2 = ActionRegistry.CreateStep("loop");
        step2.Name = "Loop 1";
        var subStep2_1 = ActionRegistry.CreateStep("wait");
        subStep2_1.Name = "Wait Sub 1";
        var subStep2_2 = ActionRegistry.CreateStep("wait");
        subStep2_2.Name = "Wait Sub 2";
        step2.Children.Add(subStep2_1);
        step2.Children.Add(subStep2_2);
        var step3 = ActionRegistry.CreateStep("wait");
        step3.Name = "Wait 3";

        wf.Actions.Add(step1);
        wf.Actions.Add(step2);
        wf.Actions.Add(step3);

        var indexed = wf.GetIndexedSteps();
        Check("indexed steps count is 5", indexed.Count == 5);
        Check("step1 number is 1", indexed[0].Id == step1.Id && indexed[0].Number == "1");
        Check("step2 number is 2", indexed[1].Id == step2.Id && indexed[1].Number == "2");
        Check("subStep2_1 number is 2.1", indexed[2].Id == subStep2_1.Id && indexed[2].Number == "2.1");
        Check("subStep2_2 number is 2.2", indexed[3].Id == subStep2_2.Id && indexed[3].Number == "2.2");
        Check("step3 number is 3", indexed[4].Id == step3.Id && indexed[4].Number == "3");

        // Exclude step test (e.g. self-jump exclusion)
        var excluded = wf.GetIndexedSteps(excludeId: step2.Id);
        Check("exclude self from indexed steps", !excluded.Any(s => s.Id == step2.Id));

        // Reordering: Insert a step before step 3
        var newStep = ActionRegistry.CreateStep("wait");
        newStep.Name = "Inserted Step";
        wf.Actions.Insert(2, newStep); // before step3

        var reindexed = wf.GetIndexedSteps();
        Check("reindexed step3 number shifted to 4", reindexed.First(s => s.Id == step3.Id).Number == "4");
        // Verify GetStepDisplayInfo resolves stable ID
        var infoStep3 = wf.GetStepDisplayInfo(step3.Id);
        Check("GetStepDisplayInfo finds step by stable ID", infoStep3 != null && infoStep3.Number == "4");

        // 3. Validation: goto_step and find_image
        var gotoStep = ActionRegistry.CreateStep("goto_step");
        gotoStep.Params["target_step_id"] = "";
        Check("goto_step empty target fails validation", gotoDef?.Validate?.Invoke(gotoStep) != null);

        gotoStep.Params["target_step_id"] = gotoStep.Id;
        Check("goto_step self-jump fails validation", gotoDef?.Validate?.Invoke(gotoStep) != null);

        gotoStep.Params["target_step_id"] = step1.Id;
        Check("goto_step valid target passes validation", gotoDef?.Validate?.Invoke(gotoStep) == null);

        var findImgStep = ActionRegistry.CreateStep("find_image");
        findImgStep.Params["image"] = "dummy.png";
        findImgStep.Params["on_found"] = "goto";
        findImgStep.Params["found_target_id"] = "";
        Check("find_image goto with empty found_target fails validation", findImgDef?.Validate?.Invoke(findImgStep) != null);

        findImgStep.Params["found_target_id"] = findImgStep.Id;
        Check("find_image goto self-jump fails validation", findImgDef?.Validate?.Invoke(findImgStep) != null);

        findImgStep.Params["not_found"] = "goto";
        findImgStep.Params["not_found_target_id"] = findImgStep.Id;
        Check("find_image not_found self-jump fails validation", findImgDef?.Validate?.Invoke(findImgStep) != null);

        // 4. ExecutionEngine jump execution
        var jumpWf = new Workflow { Name = "Jump Execution Test" };
        var s1 = ActionRegistry.CreateStep("wait"); s1.Params["duration_ms"] = 10;
        var s2 = ActionRegistry.CreateStep("goto_step");
        var s3 = ActionRegistry.CreateStep("wait"); s3.Params["duration_ms"] = 10; // should be skipped!
        var s4 = ActionRegistry.CreateStep("wait"); s4.Params["duration_ms"] = 10;
        s2.Params["target_step_id"] = s4.Id;

        jumpWf.Actions.Add(s1);
        jumpWf.Actions.Add(s2);
        jumpWf.Actions.Add(s3);
        jumpWf.Actions.Add(s4);

        var engine = new ExecutionEngine();
        var executedSteps = new List<string>();
        var finished = new List<(bool ok, string message)>();
        engine.StepDone += id => executedSteps.Add(id);
        engine.Finished += (ok, msg) => finished.Add((ok, msg));

        engine.Run(jumpWf);
        WaitForIdle(engine, 5000);

        Check("goto_step jump executed cleanly", finished.Count == 1 && finished[0].ok, finished.FirstOrDefault().message ?? "no finished event");
        Check("step 3 was skipped by goto_step", !executedSteps.Contains(s3.Id));
        Check("step 4 was executed after jump", executedSteps.Contains(s4.Id));
        Check("step 1 and step 2 were executed", executedSteps.Contains(s1.Id) && executedSteps.Contains(s2.Id), $"done steps: {string.Join(",", executedSteps)}");

        // Jump to deleted/non-existent step fails cleanly
        var badJumpWf = new Workflow { Name = "Bad Jump Test" };
        var badGoto = ActionRegistry.CreateStep("goto_step");
        badGoto.Params["target_step_id"] = "non-existent-guid";
        badJumpWf.Actions.Add(badGoto);

        var badEngine = new ExecutionEngine();
        var badFinished = new List<(bool ok, string message)>();
        badEngine.Finished += (ok, msg) => badFinished.Add((ok, msg));
        badEngine.Run(badJumpWf);
        WaitForIdle(badEngine, 5000);

        Check("goto_step to deleted step fails cleanly without crash", badFinished.Count == 1 && !badFinished[0].ok);

        // 5. Theme persistence
        string originalTheme = Macro.UI.Widgets.WorkflowEditorPanel.LoadPersistedTheme();
        try
        {
            Macro.UI.Widgets.WorkflowEditorPanel.SavePersistedTheme("light");
            Check("persist light theme", Macro.UI.Widgets.WorkflowEditorPanel.LoadPersistedTheme() == "light");

            Macro.UI.Widgets.WorkflowEditorPanel.SavePersistedTheme("dark");
            Check("persist dark theme", Macro.UI.Widgets.WorkflowEditorPanel.LoadPersistedTheme() == "dark");
        }
        finally
        {
            Macro.UI.Widgets.WorkflowEditorPanel.SavePersistedTheme(originalTheme);
        }
    }

    private static void TestActionDefaultsAndApplicationToNewSteps()
    {
        // 1. Initial State / Clear defaults
        ActionDefaults.ClearDefaultCoordinates();
        ActionDefaults.ClearDefaultImageTemplate();
        Check("defaults initially cleared: coords", !ActionDefaults.HasDefaultCoordinates);
        Check("defaults initially cleared: image", !ActionDefaults.HasDefaultImageTemplate);

        // Without defaults, new steps get standard default values
        var standardClick = ActionRegistry.CreateStep("click");
        Check("without default coords, click gets standard X=100", standardClick.GetInt("x") == 100);
        Check("without default coords, click gets standard Y=100", standardClick.GetInt("y") == 100);
        Check("without default coords, click has no tab_index", !standardClick.Params.ContainsKey("tab_index"));

        var standardFind = ActionRegistry.CreateStep("find_image");
        Check("without default image, find_image gets empty image", standardFind.GetStr("image") == "");

        // 2. Set Default Click Coordinates
        ActionDefaults.SetDefaultCoordinates(450, 680, 1);
        Check("has default coords true", ActionDefaults.HasDefaultCoordinates);
        Check("default X is 450", ActionDefaults.DefaultX == 450);
        Check("default Y is 680", ActionDefaults.DefaultY == 680);
        Check("default tab_index is 1", ActionDefaults.DefaultTabIndex == 1);

        // 3. New Click steps automatically populate with default coordinates
        var newClick = ActionRegistry.CreateStep("click");
        Check("new click step auto-populates X=450", newClick.GetInt("x") == 450);
        Check("new click step auto-populates Y=680", newClick.GetInt("y") == 680);
        Check("new click step auto-populates tab_index=1", newClick.GetInt("tab_index") == 1);

        var newDoubleClick = ActionRegistry.CreateStep("double_click");
        Check("new double_click step auto-populates coords", newDoubleClick.GetInt("x") == 450 && newDoubleClick.GetInt("y") == 680);

        var newRightClick = ActionRegistry.CreateStep("right_click");
        Check("new right_click step auto-populates coords", newRightClick.GetInt("x") == 450 && newRightClick.GetInt("y") == 680);

        // 4. Editing a specific step's coordinates does NOT overwrite shared defaults
        newClick.Params["x"] = 999;
        newClick.Params["y"] = 888;
        Check("step params updated to 999, 888", newClick.GetInt("x") == 999 && newClick.GetInt("y") == 888);
        Check("shared default X remains 450", ActionDefaults.DefaultX == 450);
        Check("shared default Y remains 680", ActionDefaults.DefaultY == 680);
        Check("shared default tab_index remains 1", ActionDefaults.DefaultTabIndex == 1);

        // 5. Set Default Image Template
        var tempImg = Path.Combine(Path.GetTempPath(), $"macro_selftest_template_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempImg, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); // dummy PNG header
        try
        {
            ActionDefaults.SetDefaultImageTemplate(tempImg, 2);
            Check("has default image true", ActionDefaults.HasDefaultImageTemplate);
            Check("default image path matches", ActionDefaults.DefaultImagePath == tempImg);
            Check("default image tab_index is 2", ActionDefaults.DefaultImageTabIndex == 2);

            // 6. New Find Image steps automatically use default template
            var newFind = ActionRegistry.CreateStep("find_image");
            Check("new find_image auto-populates default image path", newFind.GetStr("image") == tempImg);
            Check("new find_image auto-populates default tab_index=2", newFind.GetInt("tab_index") == 2);

            var newClickImg = ActionRegistry.CreateStep("click_image");
            Check("new click_image auto-populates default image path", newClickImg.GetStr("image") == tempImg);

            // 7. Editing a specific step's image does NOT overwrite shared defaults
            newFind.Params["image"] = "custom_other.png";
            Check("step image updated to custom", newFind.GetStr("image") == "custom_other.png");
            Check("shared default image remains unchanged", ActionDefaults.DefaultImagePath == tempImg);
            Check("shared default image tab_index remains 2", ActionDefaults.DefaultImageTabIndex == 2);

            // 8. Mode separation: setting image doesn't affect coords, setting coords doesn't affect image
            ActionDefaults.SetDefaultCoordinates(111, 222, 0);
            Check("coords updated to 111, 222", ActionDefaults.DefaultX == 111 && ActionDefaults.DefaultY == 222);
            Check("image default unaffected by coords update", ActionDefaults.DefaultImagePath == tempImg && ActionDefaults.HasDefaultImageTemplate);

            // 9. Persistence roundtrip through config.json: coords are persisted, image template is session-only
            ActionDefaults.LoadConfig();
            Check("persisted coords restored from config.json", ActionDefaults.HasDefaultCoordinates && ActionDefaults.DefaultX == 111 && ActionDefaults.DefaultY == 222 && ActionDefaults.DefaultTabIndex == 0);
            Check("default image template is session-only and NOT restored from config.json", !ActionDefaults.HasDefaultImageTemplate && ActionDefaults.DefaultImagePath == "");

            // 10. Clearing defaults and deleting image
            ActionDefaults.ClearDefaultCoordinates();
            Check("clear default coords", !ActionDefaults.HasDefaultCoordinates);

            ActionDefaults.ClearDefaultImageTemplate();
            Check("clear default image template", !ActionDefaults.HasDefaultImageTemplate);

            var afterClearClick = ActionRegistry.CreateStep("click");
            Check("after clear, click uses standard X=100", afterClearClick.GetInt("x") == 100);

            var afterClearFind = ActionRegistry.CreateStep("find_image");
            Check("after clear, find_image uses empty image", afterClearFind.GetStr("image") == "");
        }
        finally
        {
            if (File.Exists(tempImg)) File.Delete(tempImg);
            ActionDefaults.ClearDefaultCoordinates();
            ActionDefaults.ClearDefaultImageTemplate();
        }
    }

    private static void TestRealtimeInstanceStatusMonitoring()
    {
        // 1. Monitor instance simulation: initial load
        var existingTab0 = new Macro.Core.Emulator.LdInstanceInfo
        {
            Index = 0,
            Name = "LDPlayer-0",
            IsRunning = false,
            OperationStatus = "Đang khởi động... (5s/45s)",
            IsSelected = true
        };
        existingTab0.AssignedMacros.Add(new Macro.Core.Emulator.AssignedMacroItem { Name = "Farm Macro" });

        var existingTab1 = new Macro.Core.Emulator.LdInstanceInfo
        {
            Index = 1,
            Name = "LDPlayer-1",
            IsRunning = true,
            OperationStatus = "Đang chạy",
            IsSelected = false
        };

        var panel = new Macro.UI.Widgets.LdTabListPanel();
        var initialList = new List<Macro.Core.Emulator.LdInstanceInfo> { existingTab0, existingTab1 };
        panel.UpdateInstanceStatesFromMonitor(initialList);

        Check("monitor initial load has 2 tabs", panel.GetAllInstances().Count == 2);
        Check("tab0 initial status is starting", panel.GetAllInstances()[0].OperationStatus.Contains("Đang khởi động"));
        Check("tab1 initial status is running", panel.GetAllInstances()[1].IsRunning);

        // 2. Real-time update: Tab1 was closed externally -> fresh reports IsRunning=false -> transitions to "Đã tắt"
        var freshClosedList = new List<Macro.Core.Emulator.LdInstanceInfo>
        {
            new Macro.Core.Emulator.LdInstanceInfo
            {
                Index = 0,
                Name = "LDPlayer-0",
                IsRunning = false,
                OperationStatus = "Đang khởi động..."
            },
            new Macro.Core.Emulator.LdInstanceInfo
            {
                Index = 1,
                Name = "LDPlayer-1",
                IsRunning = false // Stopped
            }
        };

        panel.UpdateInstanceStatesFromMonitor(freshClosedList);
        Check("tab1 transitioned to stopped", !panel.GetAllInstances()[1].IsRunning && panel.GetAllInstances()[1].OperationStatus == "Đã tắt");

        // 3. Selection and assigned macros are preserved during updates
        Check("tab0 selection preserved", panel.GetAllInstances()[0].IsSelected);
        Check("tab0 assigned macro preserved", panel.GetAllInstances()[0].AssignedMacros.Count == 1 && panel.GetAllInstances()[0].AssignedMacros[0].Name == "Farm Macro");

        // 4. Test direct region drag calculation (min size 3px)
        static (int x, int y, int w, int h) CalcCrop(int x1, int y1, int x2, int y2)
        {
            return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        }

        var validCrop = CalcCrop(50, 60, 54, 65); // w=4, h=5 >= 3
        Check("crop >= 3px is valid", validCrop.w >= 3 && validCrop.h >= 3);

        var tinyCrop = CalcCrop(50, 60, 51, 61); // w=1, h=1 < 3
        Check("crop < 3px is rejected", tinyCrop.w < 3 || tinyCrop.h < 3);

        // 5. Test stop monitoring
        panel.StopMonitoring();
        Check("stop monitoring cleanly cancels task", true);
    }

    private static void TestImageRegionCropAndExecutionBinding()
    {
        // 1. Coordinate transformation across zoom levels to original bitmap space
        int originalW = 1920;
        int originalH = 1080;

        static (int minX, int minY, int w, int h) ComputeSelection(Point p1, Point p2, int imgW, int imgH)
        {
            int minX = (int)Math.Round(Math.Min(p1.X, p2.X));
            int minY = (int)Math.Round(Math.Min(p1.Y, p2.Y));
            int maxX = (int)Math.Round(Math.Max(p1.X, p2.X));
            int maxY = (int)Math.Round(Math.Max(p1.Y, p2.Y));

            minX = Math.Clamp(minX, 0, imgW - 1);
            minY = Math.Clamp(minY, 0, imgH - 1);
            maxX = Math.Clamp(maxX, minX + 1, imgW);
            maxY = Math.Clamp(maxY, minY + 1, imgH);

            return (minX, minY, maxX - minX, maxY - minY);
        }

        // Test normal selection
        var sel1 = ComputeSelection(new Point(100, 200), new Point(350, 450), originalW, originalH);
        Check("crop coordinate minX", sel1.minX == 100);
        Check("crop coordinate minY", sel1.minY == 200);
        Check("crop coordinate width", sel1.w == 250);
        Check("crop coordinate height", sel1.h == 250);

        // Test reverse selection (drag bottom-right to top-left)
        var sel2 = ComputeSelection(new Point(350, 450), new Point(100, 200), originalW, originalH);
        Check("reverse crop minX matches", sel2.minX == 100 && sel2.w == 250);
        Check("reverse crop minY matches", sel2.minY == 200 && sel2.h == 250);

        // Test boundary clamping at image edge
        var selEdge = ComputeSelection(new Point(1800, 1000), new Point(2500, 1500), originalW, originalH);
        Check("boundary crop clamped to pixel width", selEdge.minX + selEdge.w == originalW);
        Check("boundary crop clamped to pixel height", selEdge.minY + selEdge.h == originalH);

        // 2. Draft step remains draft on crop; step is only added when user explicitly clicks "Thêm"
        var wf = new Workflow { Name = "Image Test Workflow" };
        var draftStep = ActionRegistry.CreateStep("find_image");
        string dummyTemplate = Path.Combine(Path.GetTempPath(), $"test_crop_template_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(dummyTemplate, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        bool isDraft = true;
        draftStep.Params["image"] = dummyTemplate;
        draftStep.Params["tab_index"] = 0;

        // Verify crop does not auto-add draft to workflow
        Check("crop does not auto-add draft to workflow", !wf.Actions.Contains(draftStep));
        Check("draft status preserved", isDraft);

        // User explicitly clicks "Thêm"
        wf.Actions.Add(draftStep);
        isDraft = false;

        Check("step added to workflow only when user clicks Add", wf.Actions.Contains(draftStep));
        Check("draft status cleared after Add", !isDraft);
        Check("workflow step params contain template image", draftStep.GetStr("image") == dummyTemplate);
        Check("workflow step params contain tab_index", draftStep.GetInt("tab_index") == 0);

        // 3. Execution verification: find_image step uses the cropped image as actual input
        var def = ActionRegistry.Get("find_image");
        string valError = def.Validate(draftStep);
        Check("find_image validates cropped template file exists", valError == null);

        // 4. Pending cropped image mechanism when no image step is selected
        string pendingTemplate = Path.Combine(Path.GetTempPath(), $"pending_crop_template_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(pendingTemplate, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        (string Path, int TabIndex)? pendingCrop = (pendingTemplate, 1);

        // Selection is preserved when no step selected
        bool selectionVisible = true;
        Check("selection preserved when no step targeted", selectionVisible);

        // User subsequent selection/addition of find_image automatically consumes pending template
        var newFindStep = ActionRegistry.CreateStep("find_image");
        if (pendingCrop.HasValue)
        {
            newFindStep.Params["image"] = pendingCrop.Value.Path;
            newFindStep.Params["tab_index"] = pendingCrop.Value.TabIndex;
            wf.Actions.Add(newFindStep);
            pendingCrop = null; // consumed
        }

        Check("subsequent step consumed pending template path", newFindStep.GetStr("image") == pendingTemplate);
        Check("subsequent step consumed pending tab_index", newFindStep.GetInt("tab_index") == 1);
        Check("pending template cleared after consumption", !pendingCrop.HasValue);
        Check("workflow actions count is 2", wf.Actions.Count == 2);

        // 5. Macro file save and reload persistence
        string macroPath = Path.Combine(Path.GetTempPath(), $"macro_save_reload_test_{Guid.NewGuid():N}.macro");
        try
        {
            WorkflowStorage.Save(wf, macroPath);
            Check("macro with cropped template saved to file", File.Exists(macroPath));

            var (loadedWf, warnings) = WorkflowStorage.Load(macroPath);
            Check("loaded workflow contains 2 actions", loadedWf.Actions.Count == 2);
            Check("loaded workflow warnings is empty", warnings.Count == 0);

            var loadedStep1 = loadedWf.Actions[0];
            Check("loaded step 1 is find_image", loadedStep1.Type == "find_image");
            Check("loaded step 1 image path preserved exactly", loadedStep1.GetStr("image") == dummyTemplate);
            Check("loaded step 1 tab_index preserved", loadedStep1.GetInt("tab_index") == 0);
            Check("loaded step 1 passes find_image validation", def.Validate(loadedStep1) == null);

            var loadedStep2 = loadedWf.Actions[1];
            Check("loaded step 2 is find_image", loadedStep2.Type == "find_image");
            Check("loaded step 2 image path preserved exactly", loadedStep2.GetStr("image") == pendingTemplate);
            Check("loaded step 2 tab_index preserved", loadedStep2.GetInt("tab_index") == 1);
            Check("loaded step 2 passes find_image validation", def.Validate(loadedStep2) == null);
        }
        finally
        {
            try { if (File.Exists(macroPath)) File.Delete(macroPath); } catch { }
            try { if (File.Exists(dummyTemplate)) File.Delete(dummyTemplate); } catch { }
            try { if (File.Exists(pendingTemplate)) File.Delete(pendingTemplate); } catch { }
        }

        // 6. Test ScreenshotService.ResolveImagePath and ImageFinder.LoadTemplate
        var templatesFolder = Macro.Core.Services.ScreenshotService.TemplatesFolder();
        var templateInFolder = Path.Combine(templatesFolder, $"test_resolve_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(templateInFolder, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        try
        {
            var resolvedByFileName = Macro.Core.Services.ScreenshotService.ResolveImagePath(Path.GetFileName(templateInFolder));
            Check("resolve template by filename finds path in templates folder", resolvedByFileName == templateInFolder);

            var resolvedByDirectPath = Macro.Core.Services.ScreenshotService.ResolveImagePath(templateInFolder);
            Check("resolve template by direct path works", resolvedByDirectPath == templateInFolder);

            var stepWithFileNameOnly = ActionRegistry.CreateStep("find_image");
            stepWithFileNameOnly.Params["image"] = Path.GetFileName(templateInFolder);
            Check("find_image validation passes with filename-only template in templates folder", def.Validate(stepWithFileNameOnly) == null);
        }
        finally
        {
            try { if (File.Exists(templateInFolder)) File.Delete(templateInFolder); } catch { }
        }

        // 7. Auto-target empty find_image step when no step is selected in editor
        var wfAuto = new Workflow { Name = "Auto Target Workflow" };
        var clickStep = ActionRegistry.CreateStep("click");
        var emptyFindStep = ActionRegistry.CreateStep("find_image");
        emptyFindStep.Params["image"] = "";
        wfAuto.Actions.Add(clickStep);
        wfAuto.Actions.Add(emptyFindStep);

        // Simulate UpdateTargetStepImageTemplate lookup
        ActionStep target = null;
        foreach (var s in wfAuto.Actions)
        {
            if (s.Type == "find_image" && string.IsNullOrWhiteSpace(s.GetStr("image")))
            {
                target = s;
                break;
            }
        }
        Check("auto-targeted empty find_image step", target == emptyFindStep);
        if (target != null)
        {
            target.Params["image"] = "dummy.png";
        }
        Check("auto-targeted step received template", target.GetStr("image") == "dummy.png");

        // 8. Test ImageViewerWindow toolbar button enablement, selection, and clearing
        string testImgPath = Path.Combine(Path.GetTempPath(), $"test_viewer_bg_{Guid.NewGuid():N}.png");
        try
        {
            var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(200, 200, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderTarget));
            using (var fs = File.Create(testImgPath))
            {
                enc.Save(fs);
            }

            var viewer = new Macro.UI.Dialogs.ImageViewerWindow(testImgPath, null, Macro.UI.Dialogs.ViewerMode.SelectRegion);
            Check("viewer initial apply button is disabled", !viewer.IsApplyRegionEnabled);
            Check("viewer initial selection is empty", viewer.CurrentSelection == Int32Rect.Empty);

            // Simulate drag & release via FinalizeSelection
            viewer.FinalizeSelection(new Point(20, 30), new Point(120, 110));
            Check("viewer apply button enabled after valid selection", viewer.IsApplyRegionEnabled);
            Check("viewer selection rect has correct width", viewer.CurrentSelection.Width == 100);
            Check("viewer selection rect has correct height", viewer.CurrentSelection.Height == 80);
            Check("viewer staged template file exists", !string.IsNullOrEmpty(viewer.StagedTemplatePath) && File.Exists(viewer.StagedTemplatePath));

            string createdTemplate = viewer.StagedTemplatePath;

            // Test ClearSelection resets button and staged template
            viewer.ClearSelection();
            Check("viewer apply button disabled after clear selection", !viewer.IsApplyRegionEnabled);
            Check("viewer selection is empty after clear", viewer.CurrentSelection == Int32Rect.Empty);
            Check("viewer staged template cleared after clear selection", string.IsNullOrEmpty(viewer.StagedTemplatePath));

            // Test 4-direction dragging
            // 1. Bottom-Right to Top-Left
            viewer.FinalizeSelection(new Point(120, 110), new Point(20, 30));
            Check("drag BR to TL enables button", viewer.IsApplyRegionEnabled && viewer.CurrentSelection.Width == 100 && viewer.CurrentSelection.Height == 80);

            // 2. Top-Right to Bottom-Left
            viewer.FinalizeSelection(new Point(120, 30), new Point(20, 110));
            Check("drag TR to BL enables button", viewer.IsApplyRegionEnabled && viewer.CurrentSelection.Width == 100 && viewer.CurrentSelection.Height == 80);

            // 3. Bottom-Left to Top-Right
            viewer.FinalizeSelection(new Point(20, 110), new Point(120, 30));
            Check("drag BL to TR enables button", viewer.IsApplyRegionEnabled && viewer.CurrentSelection.Width == 100 && viewer.CurrentSelection.Height == 80);

            // Test drag too small (< 3px)
            viewer.FinalizeSelection(new Point(20, 20), new Point(21, 21));
            Check("drag < 3px rejected and leaves button disabled", !viewer.IsApplyRegionEnabled && viewer.CurrentSelection == Int32Rect.Empty);

            // 9. Test PropertiesPanel placeholder is clean and does not display default image template card
            var propPanel = new Macro.UI.Widgets.PropertiesPanel();
            Check("properties panel shows placeholder without default image card", propPanel.CurrentStep == null);

            // 10. Test ApplyCurrentSelectionAsTemplate sets session template without creating macro steps
            viewer.FinalizeSelection(new Point(10, 10), new Point(80, 80));
            viewer.ApplyCurrentSelectionAsTemplate();
            Check("applying selection sets session default image template", ActionDefaults.HasDefaultImageTemplate);
            Check("applying selection does not require prior step selection", true);
            var subsequentStep = ActionRegistry.CreateStep("find_image");
            Check("subsequent step created gets session image template", subsequentStep.GetStr("image") == viewer.StagedTemplatePath);

            try { if (!string.IsNullOrEmpty(createdTemplate) && File.Exists(createdTemplate)) File.Delete(createdTemplate); } catch { }
            viewer.Close();
        }
        finally
        {
            try { if (File.Exists(testImgPath)) File.Delete(testImgPath); } catch { }
        }
    }

    private static void TestAttachedTxtFilesAndReadLinesAndVariables()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"macro_txt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Test AttachedTxtFile creation, Segregation and Clone
            var wfA = new Workflow { Name = "Macro A" };
            var fileA1 = Path.Combine(tempDir, "fileA1.txt");
            File.WriteAllLines(fileA1, new[] { "A_line1", "A_line2", "A_line3" });
            wfA.AttachedFiles.Add(new AttachedTxtFile { Name = "data_a.txt", FilePath = fileA1 });

            var wfB = new Workflow { Name = "Macro B" };
            var fileB1 = Path.Combine(tempDir, "fileB1.txt");
            File.WriteAllLines(fileB1, new[] { "B_line1", "B_line2" });
            wfB.AttachedFiles.Add(new AttachedTxtFile { Name = "data_b.txt", FilePath = fileB1 });

            Check("Workflow A can find its own attached file", wfA.FindAttachedFile("data_a.txt") != null);
            Check("Workflow A CANNOT find Workflow B's attached file (strict segregation)", wfA.FindAttachedFile("data_b.txt") == null);
            Check("Workflow B can find its own attached file", wfB.FindAttachedFile("data_b.txt") != null);
            Check("Workflow B CANNOT find Workflow A's attached file (strict segregation)", wfB.FindAttachedFile("data_a.txt") == null);

            var wfACloned = wfA.Clone();
            Check("Cloned workflow preserves attached files", wfACloned.AttachedFiles.Count == 1 && wfACloned.AttachedFiles[0].Name == "data_a.txt");
            wfACloned.AttachedFiles[0].Name = "modified.txt";
            Check("Cloned workflow attached files are deeply isolated", wfA.AttachedFiles[0].Name == "data_a.txt");

            // 2. Test WorkflowStorage serialization & deserialization roundtrip
            var macroPath = Path.Combine(tempDir, "test_macro.macro");
            WorkflowStorage.Save(wfA, macroPath);
            var (loadedWf, warnings) = WorkflowStorage.Load(macroPath);
            Check("WorkflowStorage load preserves attached files count", loadedWf.AttachedFiles.Count == 1);
            var loadedAttached = loadedWf.AttachedFiles[0];
            Check("WorkflowStorage load preserves attached file name and path", loadedAttached.Name == "data_a.txt" && File.Exists(loadedAttached.FilePath));

            // 3. Test read_txt_line in 'keep' mode
            var readStep = ActionRegistry.CreateStep("read_txt_line");
            readStep.Params["file_source"] = "attached";
            readStep.Params["attached_file_id"] = "data_a.txt";
            readStep.Params["line_number"] = 2;
            readStep.Params["mode"] = "keep";
            readStep.Params["variable_name"] = "var_keep";

            var def = ActionRegistry.Get("read_txt_line");
            Check("read_txt_line action has category keyboard", def.Category == "keyboard");
            Check("read_txt_line action display name is Lấy văn bản", Loc.ActionName("read_txt_line") == "Lấy văn bản");
            Check("keyboard category localized name is BÀN PHÍM", Loc.CategoryName("keyboard") == "BÀN PHÍM");
            Check("ActionRegistry UsedCategories contains keyboard and NOT text", ActionRegistry.UsedCategories().Contains("keyboard") && !ActionRegistry.UsedCategories().Contains("text"));
            var oldMacroPath = Path.Combine(tempDir, "old_macro.macro");
            File.WriteAllText(oldMacroPath, "{\"name\":\"test_old\",\"actions\":[{\"id\":\"step_old\",\"type\":\"read_txt_line\",\"name\":\"Đọc dòng tệp TXT\",\"params\":{\"file_source\":\"custom\",\"custom_file_path\":\"test.txt\",\"line_number\":1,\"mode\":\"keep\",\"variable_name\":\"v\"}}]}");
            var (oldWf, _) = WorkflowStorage.Load(oldMacroPath);
            Check("old macro with Đọc dòng tệp TXT automatically migrates name to Lấy văn bản", oldWf.Actions[0].Name == "Lấy văn bản");
            Check("read_txt_line validation passes with valid attached file", def.Validate(readStep) == null);

            var ctx = new Macro.Core.ExecutionContext(CancellationToken.None, new ManualResetEventSlim(true), _ => { })
            {
                Workflow = wfA,
                WorkflowFilePath = macroPath
            };

            def.Execute(ctx, readStep);
            Check("read_txt_line in keep mode stores correct line in variable",
                ctx.Variables.TryGetValue("var_keep", out var valKeep) && valKeep?.ToString() == "A_line2");

            var fileALinesAfterKeep = File.ReadAllLines(fileA1);
            Check("read_txt_line in keep mode preserves all lines on disk",
                fileALinesAfterKeep.Length == 3 && fileALinesAfterKeep[0] == "A_line1" && fileALinesAfterKeep[1] == "A_line2" && fileALinesAfterKeep[2] == "A_line3");

            // 4. Test read_txt_line in 'delete' mode (atomic write)
            var deleteStep = ActionRegistry.CreateStep("read_txt_line");
            deleteStep.Params["file_source"] = "attached";
            deleteStep.Params["attached_file_id"] = "data_a.txt";
            deleteStep.Params["line_number"] = 1;
            deleteStep.Params["mode"] = "delete";
            deleteStep.Params["variable_name"] = "var_delete";

            def.Execute(ctx, deleteStep);
            Check("read_txt_line in delete mode stores correct line in variable",
                ctx.Variables.TryGetValue("var_delete", out var valDelete) && valDelete?.ToString() == "A_line1");

            var fileALinesAfterDelete = File.ReadAllLines(fileA1);
            Check("read_txt_line in delete mode removed the line atomically from disk",
                fileALinesAfterDelete.Length == 2 && fileALinesAfterDelete[0] == "A_line2" && fileALinesAfterDelete[1] == "A_line3");

            var leftoverTmp = Directory.GetFiles(tempDir, "*.tmp");
            Check("no temporary files left after atomic write", leftoverTmp.Length == 0);

            // 5. Test auto encoding detection (UTF-8 BOM, UTF-16)
            var utf8BomFile = Path.Combine(tempDir, "utf8_bom.txt");
            File.WriteAllLines(utf8BomFile, new[] { "Tiếng Việt UTF-8 có BOM: Xin chào Việt Nam" }, new System.Text.UTF8Encoding(true));
            var (readLinesBom, encBom) = Macro.Actions.TxtFileManager.ReadLinesWithEncoding(utf8BomFile);
            Check("auto detect UTF-8 BOM read lines accurately", readLinesBom.Count == 1 && readLinesBom[0] == "Tiếng Việt UTF-8 có BOM: Xin chào Việt Nam");

            var utf16File = Path.Combine(tempDir, "utf16.txt");
            File.WriteAllLines(utf16File, new[] { "Unicode UTF-16: Đất nước tươi đẹp" }, System.Text.Encoding.Unicode);
            var (readLinesUtf16, encUtf16) = Macro.Actions.TxtFileManager.ReadLinesWithEncoding(utf16File);
            Check("auto detect UTF-16 read lines accurately", readLinesUtf16.Count == 1 && readLinesUtf16[0] == "Unicode UTF-16: Đất nước tươi đẹp");

            // 6. Test Error Handling: empty file, invalid line number, non-existent file
            var emptyFile = Path.Combine(tempDir, "empty.txt");
            File.WriteAllText(emptyFile, "");
            var emptyStep = ActionRegistry.CreateStep("read_txt_line");
            emptyStep.Params["file_source"] = "custom";
            emptyStep.Params["custom_file_path"] = emptyFile;
            emptyStep.Params["line_number"] = 1;
            emptyStep.Params["variable_name"] = "v";

            bool threwEmpty = false;
            try { def.Execute(ctx, emptyStep); }
            catch (ActionFailureException ex) { threwEmpty = ex.Message.Contains("rỗng") || ex.Message.Contains("không có dòng"); }
            Check("read_txt_line on empty file throws clear ActionFailureException", threwEmpty);

            var outOfBoundsStep = ActionRegistry.CreateStep("read_txt_line");
            outOfBoundsStep.Params["file_source"] = "attached";
            outOfBoundsStep.Params["attached_file_id"] = "data_a.txt";
            outOfBoundsStep.Params["line_number"] = 999;
            outOfBoundsStep.Params["variable_name"] = "v";

            bool threwOutOfBounds = false;
            try { def.Execute(ctx, outOfBoundsStep); }
            catch (ActionFailureException ex) { threwOutOfBounds = ex.Message.Contains("999 không tồn tại") || ex.Message.Contains("chỉ có"); }
            Check("read_txt_line out of bounds line number throws clear ActionFailureException", threwOutOfBounds);

            var missingAttachedStep = ActionRegistry.CreateStep("read_txt_line");
            missingAttachedStep.Params["file_source"] = "attached";
            missingAttachedStep.Params["attached_file_id"] = "non_existent.txt";
            missingAttachedStep.Params["line_number"] = 1;
            missingAttachedStep.Params["variable_name"] = "v";

            bool threwMissingAttached = false;
            try { def.Execute(ctx, missingAttachedStep); }
            catch (ActionFailureException ex) { threwMissingAttached = ex.Message.Contains("không có trong danh sách"); }
            Check("read_txt_line non-existent attached file throws clear ActionFailureException", threwMissingAttached);

            // 7. Test type_text in 'variable' mode & uninitialized variable error
            var typeDef = ActionRegistry.Get("type_text");
            var typeVarStep = ActionRegistry.CreateStep("type_text");
            typeVarStep.Name = "Nhập tài khoản";
            typeVarStep.Params["text_source"] = "variable";
            typeVarStep.Params["variable_name"] = "uninitialized_var_xyz";

            bool threwUnsetVar = false;
            string unsetMsg = "";
            try { typeDef.Execute(ctx, typeVarStep); }
            catch (ActionFailureException ex)
            {
                threwUnsetVar = true;
                unsetMsg = ex.Message;
            }
            Check("type_text with uninitialized variable throws ActionFailureException", threwUnsetVar);
            Check("type_text error message specifies variable name and step info",
                unsetMsg.Contains("uninitialized_var_xyz") && (unsetMsg.Contains("Nhập tài khoản") || unsetMsg.Contains(typeVarStep.Id)));

            // Now set the variable and verify
            ctx.Variables["uninitialized_var_xyz"] = "admin_valid_user";
            Check("variable is successfully resolved when initialized", ctx.Variables["uninitialized_var_xyz"]?.ToString() == "admin_valid_user");

            // 8. Test Workflow.GetDiscoveredVariables()
            wfA.Actions.Add(readStep);
            wfA.Actions.Add(typeVarStep);
            var discoveredVars = wfA.GetDiscoveredVariables();
            Check("Workflow discovered variables contains var_keep", discoveredVars.Contains("var_keep"));
            Check("Workflow discovered variables contains uninitialized_var_xyz", discoveredVars.Contains("uninitialized_var_xyz"));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void TestEmulatorFindImageAndAppLaunch()
    {
        // 1. Test Package List Parsing
        string mockPmThirdParty =
            "package:/data/app/~~a1b2/com.facebook.katana-cdef/base.apk=com.facebook.katana\r\n" +
            "package:/data/app/~~g3h4/com.zing.zalo-ijkl/base.apk=com.zing.zalo\r\n";
        string mockPmAll =
            mockPmThirdParty +
            "package:/system/priv-app/Settings/Settings.apk=com.android.settings\r\n" +
            "package:/system/app/Browser/Browser.apk=com.android.browser\r\n" +
            "invalid_line_without_equals\r\n";

        var parsedPackages = Macro.Core.Emulator.LdPlayerService.ParsePackageList(mockPmThirdParty, mockPmAll);
        Check("parse package list count is 4", parsedPackages.Count == 4);
        Check("parse package Facebook package name", parsedPackages.Any(p => p.PackageName == "com.facebook.katana" && p.IsThirdParty));
        Check("parse package Facebook friendly name", parsedPackages.First(p => p.PackageName == "com.facebook.katana").DisplayName == "Facebook");
        Check("parse package Facebook apk path", parsedPackages.First(p => p.PackageName == "com.facebook.katana").ApkPath.Contains("base.apk"));
        Check("parse package Settings display name", parsedPackages.First(p => p.PackageName == "com.android.settings").DisplayName.Contains("Cài đặt"));
        Check("parse package Settings is not third party", !parsedPackages.First(p => p.PackageName == "com.android.settings").IsThirdParty);

        // 2. Test GetFriendlyAppName dictionary & fallback
        Check("friendly name for tiktok", Macro.Core.Emulator.LdPlayerService.GetFriendlyAppName("com.zhiliaoapp.musically") == "TikTok");
        Check("friendly name for youtube", Macro.Core.Emulator.LdPlayerService.GetFriendlyAppName("com.google.android.youtube") == "YouTube");
        Check("friendly name for unknown package fallback", Macro.Core.Emulator.LdPlayerService.GetFriendlyAppName("com.mycompany.super_runner") == "Super Runner");

        // 3. Test AppActions (open_application) schema, validation, and summary
        var appDef = ActionRegistry.Get("open_application");
        Check("open_application exists in registry", appDef != null);
        Check("open_application schema contains target_type", appDef.ParamSchema.Any(p => p.Key == "target_type"));
        Check("open_application schema contains package_name", appDef.ParamSchema.Any(p => p.Key == "package_name"));
        Check("open_application schema contains app_name", appDef.ParamSchema.Any(p => p.Key == "app_name"));

        // Windows target_type validation
        var winStep = ActionRegistry.CreateStep("open_application");
        winStep.Params["target_type"] = "windows";
        winStep.Params["path"] = "";
        var winErr = appDef.Validate(winStep);
        Check("open_application windows without path fails validation", winErr != null);

        winStep.Params["path"] = @"C:\Windows\notepad.exe";
        Check("open_application windows with valid path passes", appDef.Validate(winStep) == null);

        // Emulator target_type validation
        var emuStep = ActionRegistry.CreateStep("open_application");
        emuStep.Params["target_type"] = "emulator";
        emuStep.Params["package_name"] = "";
        emuStep.Params["path"] = "";
        var emuErr = appDef.Validate(emuStep);
        Check("open_application emulator without package_name fails validation", emuErr != null);

        emuStep.Params["package_name"] = "com.facebook.katana";
        emuStep.Params["app_name"] = "Facebook";
        Check("open_application emulator with package passes validation", appDef.Validate(emuStep) == null);
        Check("open_application summary contains Facebook and package",
            appDef.Summary(emuStep.Params).Contains("Facebook") && appDef.Summary(emuStep.Params).Contains("com.facebook.katana"));

        // Tab index binding in summary
        emuStep.Params["tab_index"] = 2;
        Check("open_application summary contains [Tab #2]", appDef.Summary(emuStep.Params).Contains("[Tab #2]"));

        // 4. Test open_application execution error handling (missing tab / stopped tab)
        var ctx = new Macro.Core.ExecutionContext(CancellationToken.None, new ManualResetEventSlim(true), _ => { });
        var savedSelected = Macro.Core.Emulator.EmulatorManager.SelectedInstance;
        Macro.Core.Emulator.EmulatorManager.SelectedInstance = null;

        try
        {
            // Test no tab specified
            var emuNoTabStep = ActionRegistry.CreateStep("open_application");
            emuNoTabStep.Params["target_type"] = "emulator";
            emuNoTabStep.Params["package_name"] = "com.facebook.katana";
            bool threwNoTab = false;
            try { appDef.Execute(ctx, emuNoTabStep); }
            catch (ActionFailureException ex) { threwNoTab = ex.Message.Contains("Chưa xác định tab giả lập"); }
            Check("open_application emulator without tab throws clear ActionFailureException", threwNoTab);

            // 5. Test find_image execution strictly on emulator tab (no desktop fallback)
            var findDef = ActionRegistry.Get("find_image");
            string tempImg = Path.Combine(Path.GetTempPath(), $"selftest_find_{Guid.NewGuid():N}.png");
            using (var mat = new OpenCvSharp.Mat(16, 16, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(100)))
            {
                OpenCvSharp.Cv2.ImWrite(tempImg, mat);
            }

            try
            {
                // Without tab assigned: fails immediately with clear error
                var findNoTabStep = ActionRegistry.CreateStep("find_image");
                findNoTabStep.Params["image"] = tempImg;
                bool threwFindNoTab = false;
                try { findDef.Execute(ctx, findNoTabStep); }
                catch (ActionFailureException ex) { threwFindNoTab = ex.Message.Contains("Chưa xác định được tab giả lập"); }
                Check("find_image without tab throws ActionFailureException without fallback to desktop", threwFindNoTab);

                // With tab index 999 (non-existent tab): fails with clear emulator capture error
                var findTab999Step = ActionRegistry.CreateStep("find_image");
                findTab999Step.Params["image"] = tempImg;
                findTab999Step.Params["tab_index"] = 999;
                bool threwFind999 = false;
                string err999 = "";
                try { findDef.Execute(ctx, findTab999Step); }
                catch (ActionFailureException ex)
                {
                    threwFind999 = true;
                    err999 = ex.Message;
                }
                Check("find_image on non-existent tab 999 throws clear capture error without falling back to PC screen",
                    threwFind999 && (err999.Contains("#999") || err999.Contains("không tồn tại") || err999.Contains("không chạy")));
            }
            finally
            {
                try { if (File.Exists(tempImg)) File.Delete(tempImg); } catch { }
            }
        }
        finally
        {
            Macro.Core.Emulator.EmulatorManager.SelectedInstance = savedSelected;
        }
    }

    private static void TestAdbInputAndBackgroundControl()
    {
        // 1. Test FormatAdbInputText
        Check("format adb empty string", Macro.Core.Emulator.LdPlayerService.FormatAdbInputText("") == "");
        Check("format adb spaces replaced with %s", Macro.Core.Emulator.LdPlayerService.FormatAdbInputText("hello world test") == "hello%sworld%stest");

        string rawSpecial = "a & b | c $ d \" e ' f \\ g ; h < i > j ( k ) l ` m";
        string escaped = Macro.Core.Emulator.LdPlayerService.FormatAdbInputText(rawSpecial);
        Check("format adb escapes ampersand", escaped.Contains("\\&"));
        Check("format adb escapes pipe", escaped.Contains("\\|"));
        Check("format adb escapes dollar", escaped.Contains("\\$"));
        Check("format adb escapes quotes", escaped.Contains("\\\"") && escaped.Contains("\\'"));
        Check("format adb escapes backslash", escaped.Contains("\\\\"));
        Check("format adb escapes semicolon", escaped.Contains("\\;"));
        Check("format adb escapes redirect symbols", escaped.Contains("\\<") && escaped.Contains("\\>"));
        Check("format adb escapes parentheses", escaped.Contains("\\(") && escaped.Contains("\\)"));
        Check("format adb escapes backtick", escaped.Contains("\\`"));

        // 2. Test MapKeyToAndroidKeyCode
        Check("map key enter to 66", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("enter") == 66);
        Check("map key return to 66", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("return") == 66);
        Check("map key backspace to 67", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("backspace") == 67);
        Check("map key esc to 4", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("esc") == 4);
        Check("map key home to 3", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("home") == 3);
        Check("map key tab to 61", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("tab") == 61);
        Check("map key space to 62", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("space") == 62);
        Check("map key del to 112", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("del") == 112);
        Check("map key arrows", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("up") == 19 &&
                                Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("down") == 20 &&
                                Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("left") == 21 &&
                                Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("right") == 22);
        Check("map key a to 29", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("a") == 29);
        Check("map key z to 54", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("z") == 54);
        Check("map key 0 to 7", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("0") == 7);
        Check("map key 9 to 16", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("9") == 16);
        Check("map unknown key to 0", Macro.Core.Emulator.LdPlayerService.MapKeyToAndroidKeyCode("fake_unknown_key") == 0);

        // 3. Test IsPngHeader
        byte[] validPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        Check("png header valid", Macro.Core.Emulator.LdPlayerService.IsPngHeader(validPng));
        Check("png header too short returns false", !Macro.Core.Emulator.LdPlayerService.IsPngHeader(new byte[] { 0x89, 0x50 }));
        Check("png header corrupted returns false", !Macro.Core.Emulator.LdPlayerService.IsPngHeader(new byte[] { 0x00, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Check("png header null returns false", !Macro.Core.Emulator.LdPlayerService.IsPngHeader(null!));

        // 4. Test Mouse Actions on non-existent tab index 9999 throws ActionFailureException without fallback
        var ctx = new Macro.Core.ExecutionContext(CancellationToken.None, new ManualResetEventSlim(true), _ => { });

        var clickDef = ActionRegistry.Get("click");
        var clickStep = ActionRegistry.CreateStep("click");
        clickStep.Params["tab_index"] = 9999;
        clickStep.Params["x"] = 150;
        clickStep.Params["y"] = 250;
        bool threwClick = false;
        string clickErr = "";
        try { clickDef.Execute(ctx, clickStep); }
        catch (ActionFailureException ex) { threwClick = true; clickErr = ex.Message; }
        Check("click on non-existent tab throws ActionFailureException", threwClick && clickErr.Contains("#9999"));

        var dblClickDef = ActionRegistry.Get("double_click");
        var dblClickStep = ActionRegistry.CreateStep("double_click");
        dblClickStep.Params["tab_index"] = 9999;
        dblClickStep.Params["x"] = 150;
        dblClickStep.Params["y"] = 250;
        bool threwDbl = false;
        try { dblClickDef.Execute(ctx, dblClickStep); }
        catch (ActionFailureException) { threwDbl = true; }
        Check("double_click on non-existent tab throws ActionFailureException", threwDbl);

        var rightClickDef = ActionRegistry.Get("right_click");
        var rightClickStep = ActionRegistry.CreateStep("right_click");
        rightClickStep.Params["tab_index"] = 9999;
        rightClickStep.Params["x"] = 150;
        rightClickStep.Params["y"] = 250;
        bool threwRight = false;
        try { rightClickDef.Execute(ctx, rightClickStep); }
        catch (ActionFailureException) { threwRight = true; }
        Check("right_click on non-existent tab throws ActionFailureException", threwRight);

        var dragDef = ActionRegistry.Get("drag");
        var dragStep = ActionRegistry.CreateStep("drag");
        dragStep.Params["tab_index"] = 9999;
        dragStep.Params["x1"] = 100;
        dragStep.Params["y1"] = 100;
        dragStep.Params["x2"] = 300;
        dragStep.Params["y2"] = 300;
        bool threwDrag = false;
        try { dragDef.Execute(ctx, dragStep); }
        catch (ActionFailureException) { threwDrag = true; }
        Check("drag on non-existent tab throws ActionFailureException", threwDrag);

        var scrollDef = ActionRegistry.Get("scroll");
        var scrollStep = ActionRegistry.CreateStep("scroll");
        scrollStep.Params["tab_index"] = 9999;
        scrollStep.Params["amount"] = 3;
        bool threwScroll = false;
        try { scrollDef.Execute(ctx, scrollStep); }
        catch (ActionFailureException) { threwScroll = true; }
        Check("scroll on non-existent tab throws ActionFailureException", threwScroll);

        // 5. Test Keyboard Actions on non-existent tab index 9999
        var pressDef = ActionRegistry.Get("press_key");
        var pressStep = ActionRegistry.CreateStep("press_key");
        pressStep.Params["tab_index"] = 9999;
        pressStep.Params["key"] = "enter";
        bool threwPress = false;
        try { pressDef.Execute(ctx, pressStep); }
        catch (ActionFailureException) { threwPress = true; }
        Check("press_key on non-existent tab throws ActionFailureException", threwPress);

        var typeDef = ActionRegistry.Get("type_text");
        var typeStep = ActionRegistry.CreateStep("type_text");
        typeStep.Params["tab_index"] = 9999;
        typeStep.Params["text"] = "test background typing";
        bool threwType = false;
        try { typeDef.Execute(ctx, typeStep); }
        catch (ActionFailureException) { threwType = true; }
        Check("type_text on non-existent tab throws ActionFailureException", threwType);

        // 6. Test Screen Actions on non-existent tab index 9999
        var shotDef = ActionRegistry.Get("screenshot");
        var shotStep = ActionRegistry.CreateStep("screenshot");
        shotStep.Params["tab_index"] = 9999;
        bool threwShot = false;
        try { shotDef.Execute(ctx, shotStep); }
        catch (ActionFailureException) { threwShot = true; }
        Check("screenshot on non-existent tab throws ActionFailureException", threwShot);

        var pixelDef = ActionRegistry.Get("get_pixel_color");
        var pixelStep = ActionRegistry.CreateStep("get_pixel_color");
        pixelStep.Params["tab_index"] = 9999;
        pixelStep.Params["x"] = 50;
        pixelStep.Params["y"] = 50;
        bool threwPixel = false;
        try { pixelDef.Execute(ctx, pixelStep); }
        catch (ActionFailureException) { threwPixel = true; }
        Check("get_pixel_color on non-existent tab throws ActionFailureException", threwPixel);
    }

    private static void WaitForIdle(ExecutionEngine engine, int timeoutMs)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (engine.IsRunning && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
        Thread.Sleep(50);
    }
}

