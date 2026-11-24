using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Macro.Core;
using Macro.Core.Emulator;
using Macro.Core.Input;
using Macro.Core.Services;
using Macro.Localization;
using Macro.UI;
using Macro.UI.Dialogs;
using Macro.UI.Widgets;

namespace Macro;

public partial class MainWindow : Window
{
    private Workflow _workflow = new() { Name = "My Macro" };
    public Workflow CurrentWorkflow => _workflow;
    private string _filePath;
    private bool _dirty;
    private string _lastSelectedStepId;
    private double _leftWidth = 210;
    private double _rightWidth = 325;

    private readonly ExecutionEngine _engine = new();
    private readonly ActionLibraryPanel _library;
    private readonly LdTabListPanel _tabListPanel;
    private readonly WorkflowEditorPanel _editor;
    private readonly PropertiesPanel _properties;
    private readonly OutputPanel _output;
    private double _outputHeight = 110;
    private readonly DispatcherTimer _mouseTimer = new();
    private ImageViewerWindow _activeViewer;
    public (string Path, int TabIndex)? PendingCroppedImageTemplate { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        LogoIcon.Source = Icons.For("logo", "#4F8CFF", 20);
        BtnLeftPanel.Content = new Image
        { Source = Icons.For("panel_left", "#C7CAD1", 15), Width = 15, Height = 15 };
        BtnRightPanel.Content = new Image
        { Source = Icons.For("panel_right", "#C7CAD1", 15), Width = 15, Height = 15 };

        _library = new ActionLibraryPanel();
        _tabListPanel = new LdTabListPanel();
        _editor = new WorkflowEditorPanel();
        _properties = new PropertiesPanel();
        _properties.WorkflowProvider = () => _workflow;
        _output = new OutputPanel();
        LibraryHost.Content = _library;
        EmulatorTabsHost.Content = _tabListPanel;
        EditorHost.Content = _editor;
        PropertiesHost.Content = _properties;
        OutputHost.Content = _output;

        _tabListPanel.CaptureRequested += async inst => await CaptureTabAndOpenViewerAsync(inst);
        _tabListPanel.ConfigureRequested += () => PromptEmulatorSetup();
        _tabListPanel.OpenAdvancedManagerRequested += () => OpenEmulatorManagerOrSetup();

        ApplyTopBarLanguage();
        WirePanels();
        WireEngine();
        WireTopBar();
        WireHotkeys();

        ApplyEngineState(EngineState.Idle);
        _editor.SetWorkflow(_workflow);
        UpdateTitle();

        ActionDefaults.LoadConfig();
        EmulatorManager.LoadConfig();
        UpdateEmulatorStatus();
        EmulatorManager.ConfigurationChanged += () =>
        {
            UpdateEmulatorStatus();
            _ = _tabListPanel.RefreshTabsAsync();
        };

        Loc.LanguageChanged += ApplyLanguage;
        _mouseTimer.Interval = TimeSpan.FromMilliseconds(120);
        _mouseTimer.Tick += (_, _) => UpdateMousePosition();
        _mouseTimer.Start();

        Loaded += (_, _) =>
        {
            try { RestoreLayout(); } catch { }
            if (EmulatorManager.IsConfiguredAndValid)
            {
                _ = _tabListPanel.RefreshTabsAsync();
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    PromptEmulatorSetup();
                });
            }
        };
        Closing += OnClosing;
    }

    /// <summary>Called by Loc when the user switches language - re-renders
    /// every text in the window and all panels.</summary>
    private void ApplyLanguage()
    {
        ApplyTopBarLanguage();
        UpdateEmulatorStatus();
        _tabListPanel.ApplyLanguage();
        _library.ApplyLanguage();
        _editor.ApplyLanguage();
        _properties.ApplyLanguage();
        _output.ApplyLanguage();
        ApplyEngineState(_engine.State);
        UpdateTitle();
        FileText.Text = _filePath ?? Loc.T("status.unsaved", "Unsaved macro");
    }

    private void ApplyTopBarLanguage()
    {
        BtnNew.Content = Loc.T("top.new", "New");
        BtnNew.ToolTip = Loc.T("top.new.tip", "Start a new macro");
        BtnOpen.Content = Loc.T("top.open", "Open");
        BtnOpen.ToolTip = Loc.T("top.open.tip", "Open a saved macro");
        BtnSave.Content = Loc.T("top.save", "Save");
        BtnSave.ToolTip = Loc.T("top.save.tip", "Save the macro (Ctrl+S)");
        BtnSaveAs.Content = Loc.T("top.saveas", "Save As");
        BtnSaveAs.ToolTip = Loc.T("top.saveas.tip",
            "Save the macro to a new file");
        BtnRun.Content = Loc.T("top.run", "Run");
        BtnRun.ToolTip = Loc.T("top.run.tip", "Run the workflow");
        BtnPause.Content = Loc.T("top.pause", "Pause");
        BtnPause.ToolTip = Loc.T("top.pause.tip", "Pause / resume the run");
        BtnStop.Content = Loc.T("top.stop", "Stop");
        BtnStop.ToolTip = Loc.T("top.stop.tip", "Stop the running workflow");
        BtnCaptureScreen.Content = "📷 " + Loc.T("top.capture_screen", "Mở giao diện chụp màn hình");
        BtnCaptureScreen.ToolTip = Loc.T("top.capture_screen.tip", "Chụp màn hình tab giả lập đang chọn và mở giao diện chụp màn hình");
        BtnAttachedFiles.Content = "📄 " + Loc.T("top.attached_files", "Tệp TXT...");
        BtnAttachedFiles.ToolTip = Loc.T("top.attached_files.tip", "Quản lý các tệp .txt đính kèm cho macro này");
        BtnSettings.Content = Loc.T("top.settings", "Settings");
        BtnSettings.ToolTip = Loc.T("top.settings.tip",
            "Workflow settings (repeat, delays, failsafe)");
        BtnLeftPanel.ToolTip = Loc.T("top.leftpanel.tip",
            "Show / hide the action library");
        BtnRightPanel.ToolTip = Loc.T("top.rightpanel.tip",
            "Show / hide the properties panel");
        BtnOutput.Content = Loc.T("output.title", "OUTPUT / LOG")
            .Split('/')[0].Trim();
        BtnOutput.ToolTip = Loc.T("top.output.tip",
            "Show / hide the output panel");
    }

    // ================================================================ wiring
    private void WirePanels()
    {
        _library.ActionSelected += typeId =>
        {
            var selected = _editor.SelectedStep();
            if (selected != null)
                _lastSelectedStepId = selected.Id;
            _editor.SelectStep(null);
            var draft = ActionRegistry.CreateStep(typeId);

            if (IsImageAction(draft) && PendingCroppedImageTemplate.HasValue)
            {
                var pending = PendingCroppedImageTemplate.Value;
                draft.Params["image"] = pending.Path;
                if (pending.TabIndex >= 0)
                {
                    draft.Params["tab_index"] = pending.TabIndex;
                }
            }

            _properties.SetStep(draft, isDraft: true);
        };

        _editor.SelectionChanged += step =>
        {
            if (step != null)
            {
                _lastSelectedStepId = step.Id;
                _library.ClearSelection();
            }
            _properties.SetStep(step, isDraft: false);
        };

        _properties.AddToWorkflowRequested += step =>
        {
            var stepToAdd = step.DeepCloneWithNewIds();
            if (_lastSelectedStepId != null && _workflow.Find(_lastSelectedStepId) != null)
                _editor.AddStep(stepToAdd, afterId: _lastSelectedStepId);
            else
                _editor.AddStep(stepToAdd);

            _lastSelectedStepId = stepToAdd.Id;
            _library.ClearSelection();
            _editor.SelectStep(stepToAdd.Id);
            _properties.SetStep(stepToAdd, isDraft: false);
            MarkDirty();

            if (IsImageAction(stepToAdd))
            {
                _activeViewer?.NotifyTemplateApplied(
                    stepToAdd.Name is { Length: > 0 } ? stepToAdd.Name : Loc.ActionName(stepToAdd.Type),
                    stepToAdd.GetInt("tab_index", -1));
            }
        };

        _editor.WorkflowChanged += () => MarkDirty();

        _properties.Changed += step =>
        {
            if (!_properties.IsDraft)
            {
                _editor.RefreshCard(step.Id);
                MarkDirty();
            }
        };

        _properties.OutputSink = message => _output.Append(message);
    }

    private void WireEngine()
    {
        _engine.StateChanged += state =>
            Dispatcher.BeginInvoke(() => ApplyEngineState(state));
        _engine.StepStarted += stepId => Dispatcher.BeginInvoke(() =>
        {
            var step = _workflow.Find(stepId);
            if (step == null) return;
            CurrentStepText.Text = Loc.T("status.running_step", "Running") + ": "
                + (step.Name is { Length: > 0 } ? step.Name
                    : Loc.ActionName(step.Type));
            _editor.HighlightRunning(stepId);
        });
        _engine.StepDone += stepId =>
            Dispatcher.BeginInvoke(() => _editor.MarkDone(stepId));
        _engine.LogMessage += message =>
            Dispatcher.BeginInvoke(() =>
            {
                LogText.Text = message;
                _output.Append(message);
            });
        _engine.Finished += (ok, message) => Dispatcher.BeginInvoke(() =>
        {
            StateText.Text = ok
                ? Loc.T("status.finished", "Finished")
                : message;
            if (!ok)
                MessageBox.Show(this, message, Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void WireTopBar()
    {
        BtnNew.Click += (_, _) => NewMacro();
        BtnOpen.Click += (_, _) => OpenMacro();
        BtnSave.Click += (_, _) => SaveMacro();
        BtnSaveAs.Click += (_, _) => SaveMacroAs();
        BtnRun.Click += (_, _) => RunMacro();
        BtnPause.Click += (_, _) => TogglePause();
        BtnStop.Click += (_, _) => _engine.Stop();
        BtnCaptureScreen.Click += (_, _) => OnCaptureScreenClicked();
        BtnAttachedFiles.Click += (_, _) => OpenAttachedFilesDialog();
        BtnSettings.Click += (_, _) => OpenSettings();
        BtnEmulator.Click += (_, _) => OpenEmulatorManagerOrSetup();
        BtnFixEmulator.Click += (_, _) => PromptEmulatorSetup();
        BtnLeftPanel.Click += (_, _) => TogglePanel(0);
        BtnRightPanel.Click += (_, _) => TogglePanel(1);
        BtnOutput.Click += (_, _) => ToggleOutput();
    }

    private void OpenAttachedFilesDialog()
    {
        var dlg = new AttachedFilesDialog(_workflow, this, _filePath, () =>
        {
            MarkDirty();
            _properties.RefreshVariablesAndFiles();
        });
        dlg.ShowDialog();
    }

    private void ToggleOutput()
    {
        if (OutputBorder.Height > 0)
        {
            _outputHeight = OutputBorder.ActualHeight > 20
                ? OutputBorder.ActualHeight : _outputHeight;
            OutputBorder.Height = 0;
        }
        else
        {
            OutputBorder.Height = _outputHeight;
        }
    }

    private void WireHotkeys()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New,
            (_, _) => NewMacro()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open,
            (_, _) => OpenMacro()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save,
            (_, _) => SaveMacro()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.New, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        var runCommand = new RoutedCommand();
        runCommand.InputGestures.Add(new KeyGesture(Key.F5));
        CommandBindings.Add(new CommandBinding(runCommand, (_, _) => RunMacro()));
        var stopCommand = new RoutedCommand();
        stopCommand.InputGestures.Add(new KeyGesture(Key.F6));
        CommandBindings.Add(new CommandBinding(stopCommand, (_, _) => _engine.Stop()));
    }

    public bool IsEngineRunning => _engine.IsRunning;

    // ======================================================== engine control
    private void RunMacro()
    {
        if (_engine.IsRunning) return;
        if (_tabListPanel != null && _tabListPanel.IsRunningTabs)
        {
            MessageBox.Show(this,
                "Các tab giả lập đang chạy macro hàng loạt.\nVui lòng dừng hoặc đợi tiến trình chạy tab hoàn tất trước khi chạy macro trên giao diện chính.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EmulatorManager.IsConfiguredAndValid)
        {
            MessageBox.Show(this,
                Loc.T("emu.err.cannot_run",
                    "Chưa cấu hình trình giả lập Android (LDPlayer) hợp lệ.\n\nVui lòng thiết lập đường dẫn đến LDPlayer trước khi chạy quy trình."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PromptEmulatorSetup();
            return;
        }
        if (_workflow.Actions.Count == 0)
        {
            MessageBox.Show(this,
                Loc.T("msg.empty.workflow",
                    "The workflow is empty.\nAdd at least one action from the "
                    + "library on the left."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Determine target LDPlayer tab
        LdInstanceInfo targetInst = null;
        var selectedTabs = _tabListPanel?.GetSelectedInstances() ?? new List<LdInstanceInfo>();
        if (selectedTabs.Count == 1)
        {
            targetInst = selectedTabs[0];
        }
        else if (selectedTabs.Count > 1)
        {
            MessageBox.Show(this,
                $"Bạn đang chọn {selectedTabs.Count} tab.\nĐể chạy đồng thời nhiều tab với macro riêng, hãy dùng nút '▶ Chạy tab đã chọn' trên thanh danh sách tab.\nĐể chạy quy trình này (F5), vui lòng chỉ chọn 1 tab.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        else if (EmulatorManager.SelectedInstance != null)
        {
            targetInst = EmulatorManager.SelectedInstance;
        }
        else
        {
            var live = EmulatorManager.GetLiveInstances().Where(i => i.IsRunning).ToList();
            if (live.Count == 1)
            {
                targetInst = live[0];
            }
            else if (live.Count == 0)
            {
                MessageBox.Show(this,
                    "Không có tab giả lập LDPlayer nào đang chạy.\nVui lòng khởi động ít nhất một tab trước khi chạy macro.",
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else
            {
                MessageBox.Show(this,
                    "Có nhiều tab giả lập LDPlayer đang chạy.\nVui lòng tích chọn một tab trong danh sách để chạy macro này.",
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        if (targetInst != null && !targetInst.IsRunning)
        {
            MessageBox.Show(this,
                $"Tab #{targetInst.Index} ({targetInst.Name}) hiện đang tắt.\nVui lòng khởi động tab trước khi chạy macro.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (targetInst != null)
        {
            EmulatorManager.SelectedInstance = targetInst;
            _workflow.BindToTab(targetInst.Index);
            _engine.TargetTabIndex = targetInst.Index;
        }

        StateText.Text = Loc.T("status.running", "Running");
        _engine.WorkflowFilePath = _filePath;
        if (!_engine.Run(_workflow))
            ApplyEngineState(_engine.State);
    }

    private void TogglePause()
    {
        if (_engine.State == EngineState.Running) _engine.Pause();
        else if (_engine.State == EngineState.Paused) _engine.Resume();
    }

    private void ApplyEngineState(EngineState state)
    {
        var running = state != EngineState.Idle;
        BtnRun.IsEnabled = !running;
        BtnStop.IsEnabled = running;
        BtnPause.IsEnabled = state is EngineState.Running or EngineState.Paused;
        _editor.SetEditingEnabled(!running);

        StateDot.Fill = state switch
        {
            EngineState.Running => new SolidColorBrush(
                Color.FromRgb(0x3F, 0xB9, 0x6F)),
            EngineState.Paused or EngineState.Stopping => new SolidColorBrush(
                Color.FromRgb(0xD6, 0x9A, 0x3A)),
            _ => (Brush)FindResource("StatusDotIdle"),
        };
        StateText.Text = state switch
        {
            EngineState.Running => Loc.T("status.running", "Running"),
            EngineState.Paused => Loc.T("status.paused", "Paused"),
            EngineState.Stopping => Loc.T("status.stopping", "Stopping..."),
            _ => Loc.T("status.ready", "Ready"),
        };
        if (state == EngineState.Idle) CurrentStepText.Text = "";
        // wipe previous run highlights only when a new run begins
        if (state == EngineState.Running) _editor.ClearRunState();
    }

    // ============================================================ file menu
    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this,
            Loc.F("dlg.discard.text",
                "'{0}' has unsaved changes.\nSave them first?", _workflow.Name),
            Loc.T("dlg.app.title", "macro"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes) return SaveMacro();
        return answer == MessageBoxResult.No;
    }

    private bool ConfirmClose()
    {
        // 1. Nếu không có lệnh nào, đóng chương trình ngay lập tức, không hiện hộp thoại hỏi lưu.
        // 2. Phân biệt "có lệnh" và "có thay đổi chưa lưu": nếu đã lưu vào tệp và không có thay đổi mới, đóng ngay.
        if (!_workflow.ShouldPromptSaveOnClose(_dirty, _filePath))
            return true;

        // 3. Có ít nhất một lệnh trong danh sách hoặc nội dung đang soạn và có thay đổi chưa lưu -> hỏi người dùng
        var answer = MessageBox.Show(this,
            Loc.F("dlg.discard.text",
                "'{0}' has unsaved changes.\nSave them first?", _workflow.Name),
            Loc.T("dlg.app.title", "macro"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
            MessageBoxResult.Yes);

        // Lưu -> lưu theo cơ chế hiện có (SaveMacro), đóng nếu lưu thành công, hủy đóng nếu hủy ở dialog lưu
        if (answer == MessageBoxResult.Yes) return SaveMacro();

        // Không lưu -> đóng mà không lưu (true). Hủy đóng -> giữ chương trình mở (false).
        return answer == MessageBoxResult.No;
    }

    private void NewMacro()
    {
        if (!ConfirmDiscard()) return;
        _workflow = new Workflow { Name = "New Macro" };
        _filePath = null;
        _editor.SetWorkflow(_workflow);
        MarkDirty(false);
        FileText.Text = Loc.T("status.unsaved", "Unsaved macro");
    }

    public void TryOpenFile(string path)
    {
        try
        {
            var (workflow, warnings) = WorkflowStorage.Load(path);
            _workflow = workflow;
            _filePath = path;
            _editor.SetWorkflow(_workflow);
            MarkDirty(false);
            FileText.Text = path;
            if (warnings.Count > 0)
                MessageBox.Show(this,
                    Loc.F("dlg.fileproblems",
                        "The file contained problems:\n\n{0}",
                        string.Join("\n", warnings)),
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                Loc.F("dlg.cantopen", "Could not open file:\n{0}", ex.Message),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenMacro()
    {
        if (!ConfirmDiscard()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("dlg.open.title", "Open macro"),
            Filter = Loc.T("filter.workflow", WorkflowStorage.FileFilter),
            InitialDirectory = WorkflowStorage.DefaultFolder(),
        };
        if (dialog.ShowDialog(this) != true) return;
        TryOpenFile(dialog.FileName);
    }

    private bool SaveMacro()
    {
        if (_filePath == null) return SaveMacroAs();
        try
        {
            WorkflowStorage.Save(_workflow, _filePath);
            MarkDirty(false);
            FileText.Text = _filePath;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                Loc.F("dlg.cantsave", "Could not save file:\n{0}", ex.Message),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool SaveMacroAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("dlg.save.title", "Save macro"),
            Filter = Loc.T("filter.workflow", WorkflowStorage.FileFilter),
            InitialDirectory = WorkflowStorage.DefaultFolder(),
            FileName = (_workflow.Name is { Length: > 0 } ? _workflow.Name : "macro")
                       + ".macro",
        };
        if (dialog.ShowDialog(this) != true) return false;
        _filePath = dialog.FileName;
        return SaveMacro();
    }

    private void OpenSettings()
    {
        var dialog = new SettingsDialog(_workflow, this);
        dialog.ShowDialog();
        MarkDirty();
    }

    private void OpenEmulatorManagerOrSetup()
    {
        if (EmulatorManager.IsConfiguredAndValid)
        {
            var manager = new LdPlayerManagerDialog(this);
            manager.ShowDialog();
            _ = _tabListPanel.RefreshTabsAsync();
        }
        else
        {
            PromptEmulatorSetup();
        }
        UpdateEmulatorStatus();
    }

    private void PromptEmulatorSetup()
    {
        var dialog = new EmulatorSetupDialog(this);
        dialog.ShowDialog();
        UpdateEmulatorStatus();
    }

    private void UpdateEmulatorStatus()
    {
        if (EmulatorManager.IsConfiguredAndValid)
        {
            var name = EmulatorManager.LastResult?.EmulatorName ?? "LDPlayer";
            var version = EmulatorManager.LastResult?.VersionInfo;
            BtnEmulator.Content = $"🎮 {name}";
            BtnEmulator.Foreground = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                                     ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C));
            BtnEmulator.ToolTip = string.Format(
                Loc.T("emu.pill.tip.ready", "Trình giả lập {0} đã được liên kết:\n{1}\nBấm để mở Quản lý giả lập LDPlayer."),
                name + (string.IsNullOrEmpty(version) ? "" : $" ({version})"),
                EmulatorManager.CurrentPath);
            EmulatorWarningBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnEmulator.Content = Loc.T("emu.pill.unconfigured", "⚠ Giả lập: Chưa cấu hình");
            BtnEmulator.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F));
            BtnEmulator.ToolTip = Loc.T("emu.pill.tip.unconfigured",
                "Chưa cấu hình trình giả lập Android hợp lệ.\nBấm để thiết lập đường dẫn LDPlayer.");
            EmulatorWarningText.Text = Loc.T("emu.banner.unconfigured",
                "Chưa cấu hình trình giả lập Android (LDPlayer). Vui lòng thiết lập để chạy macro.");
            BtnFixEmulator.Content = Loc.T("emu.banner.configure", "Cấu hình ngay");
            EmulatorWarningBanner.Visibility = Visibility.Visible;
        }
    }

    // ============================================================== helpers
    private void MarkDirty(bool dirty = true)
    {
        _dirty = dirty;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = "macro"
            + (_workflow.Name is { Length: > 0 } ? $" - {_workflow.Name}" : "")
            + (_dirty ? " *" : "");
    }

    private void UpdateMousePosition()
    {
        var (x, y) = NativeInput.GetCursorPosition();
        MouseText.Text = $"Mouse: {x}, {y}";
    }

    private void TogglePanel(int which)
    {
        if (which == 0)
        {
            if (ColLibrary.Width.GridUnitType != GridUnitType.Pixel
                || ColLibrary.Width.Value > 0)
            {
                _leftWidth = ColLibrary.Width.Value;
                ColLibrary.Width = new GridLength(0);
            }
            else
            {
                ColLibrary.Width = new GridLength(_leftWidth);
            }
        }
        else
        {
            if (ColProperties.Width.GridUnitType != GridUnitType.Pixel
                || ColProperties.Width.Value > 0)
            {
                _rightWidth = ColProperties.Width.Value;
                ColProperties.Width = new GridLength(0);
            }
            else
            {
                ColProperties.Width = new GridLength(_rightWidth);
            }
        }
    }

    private void RestoreLayout() { /* window size is fine by default */ }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            e.Cancel = true;      // let the engine unwind, user closes again
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
            return;
        }
        if (!ConfirmClose())
        {
            e.Cancel = true;
            return;
        }
        try { _tabListPanel?.StopTabRuns(); } catch { }
        try { _tabListPanel?.StopMonitoring(); } catch { }
        try { _activeViewer?.Close(); } catch { }
    }

    public static bool IsImageAction(ActionStep step)
    {
        if (step == null) return false;
        return step.Type is "find_image" or "click_image" or "wait_for_image" or "image_exists"
            || step.Params.ContainsKey("image");
    }

    /// <summary>
    /// Clears any selected non-image step and ensures the properties panel
    /// displays the default image template card with the newly cropped template.
    /// </summary>
    public void ShowDefaultImageInProperties()
    {
        // Kept for backward compatibility, no longer forces placeholder wipe
    }

    /// <summary>
    /// Gets the currently targeted ActionStep: either selected in editor, or loaded in properties panel.
    /// </summary>
    public ActionStep GetCurrentlyTargetedStep()
    {
        return _editor.SelectedStep() ?? _properties.CurrentStep;
    }

    public bool IsDraftTargeted() => _properties.IsDraft && _properties.CurrentStep != null;

    /// <summary>
    /// Updates coordinates (x, y) of the currently selected action step in the macro,
    /// without creating or adding a new action step.
    /// If tabIndex >= 0, binds the coordinates to the specified LDPlayer tab.
    /// </summary>
    public bool UpdateTargetStepCoordinates(int x, int y, int tabIndex = -1)
    {
        var step = GetCurrentlyTargetedStep();
        if (step == null) return false;

        if (step.Params.ContainsKey("x") || step.Type is "click" or "double_click" or "right_click" or "move_mouse" or "drag")
        {
            step.Params["x"] = x;
            step.Params["y"] = y;
            if (tabIndex >= 0)
            {
                step.Params["tab_index"] = tabIndex;
            }
            _properties.SetStep(step, isDraft: _properties.IsDraft);
            _editor.RefreshCard(step.Id);
            MarkDirty();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Updates the image template path of the currently targeted image search step in the macro,
    /// without requiring manual confirmation, adding or re-selecting.
    /// If the step is in draft mode, it updates the draft parameters in the properties panel
    /// without auto-adding to the workflow (user explicitly clicks 'Thêm' when ready).
    /// If tabIndex >= 0, binds the search to the specified LDPlayer tab.
    /// </summary>
    public bool UpdateTargetStepImageTemplate(string templatePath, int tabIndex = -1)
    {
        string resolvedPath = ScreenshotService.ResolveImagePath(templatePath);
        if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
        {
            return false;
        }

        // 1. Check if the currently selected step in the editor is an image action
        var step = _editor.SelectedStep();

        // 2. If not, check if properties panel has an image step (draft or loaded)
        if (step == null || !IsImageAction(step))
        {
            if (_properties.CurrentStep != null && IsImageAction(_properties.CurrentStep))
            {
                step = _properties.CurrentStep;
            }
        }

        // If no image action is currently targeted, do not modify unrelated steps in workflow
        if (step == null || !IsImageAction(step))
        {
            PendingCroppedImageTemplate = (resolvedPath, tabIndex);
            return false;
        }

        // If the step is in draft mode: update draft parameters in properties panel ONLY.
        // DO NOT auto-add to workflow; user must click 'Thêm' explicitly!
        if (_properties.IsDraft && step == _properties.CurrentStep)
        {
            _properties.ApplyImageTemplate(resolvedPath, tabIndex);
            PendingCroppedImageTemplate = (resolvedPath, tabIndex);
            return true;
        }

        // Otherwise it is an existing step in the editor: update it in-place
        if (_editor.SelectedStep()?.Id != step.Id)
        {
            _editor.SelectStep(step.Id);
        }
        if (_properties.CurrentStep?.Id != step.Id)
        {
            _properties.SetStep(step, isDraft: false);
        }

        // Apply template through the exact same properties pipeline
        _properties.ApplyImageTemplate(resolvedPath, tabIndex);
        _editor.RefreshCard(step.Id);
        PendingCroppedImageTemplate = (resolvedPath, tabIndex);
        MarkDirty();
        return true;
    }

    private static ActionStep FindFirstEmptyImageStep(IEnumerable<ActionStep> steps)
    {
        foreach (var s in steps)
        {
            if (IsImageAction(s) && string.IsNullOrWhiteSpace(s.GetStr("image")))
                return s;
            var child = FindFirstEmptyImageStep(s.Children);
            if (child != null) return child;
            var elseChild = FindFirstEmptyImageStep(s.ElseChildren);
            if (elseChild != null) return elseChild;
        }
        return null;
    }

    private static ActionStep FindFirstImageStep(IEnumerable<ActionStep> steps)
    {
        foreach (var s in steps)
        {
            if (IsImageAction(s))
                return s;
            var child = FindFirstImageStep(s.Children);
            if (child != null) return child;
            var elseChild = FindFirstImageStep(s.ElseChildren);
            if (elseChild != null) return elseChild;
        }
        return null;
    }

    /// <summary>
    /// Handles the "Mở giao diện chụp màn hình" button in the top bar.
    /// Validates tab selection: requires exactly 1 tab selected, alerts if 0 or >1, and opens capture viewer.
    /// </summary>
    private async void OnCaptureScreenClicked()
    {
        if (!EmulatorManager.IsConfiguredAndValid)
        {
            MessageBox.Show(this,
                Loc.T("emu.err.cannot_capture", "Chưa cấu hình trình giả lập Android (LDPlayer).\nVui lòng cấu hình trước khi chụp màn hình."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            PromptEmulatorSetup();
            return;
        }

        var selected = _tabListPanel.GetSelectedInstances();
        if (selected.Count == 0)
        {
            MessageBox.Show(this,
                Loc.T("emu.capture.no_selection", "Vui lòng chọn một tab trước."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count > 1)
        {
            MessageBox.Show(this,
                "Chỉ được chọn một tab khi chụp màn hình.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var inst = selected[0];
        if (!inst.IsRunning)
        {
            MessageBox.Show(this,
                $"Tab #{inst.Index} ({inst.Name}) hiện đang tắt.\nVui lòng khởi động tab trước khi chụp màn hình.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await CaptureTabAndOpenViewerAsync(inst);
    }

    /// <summary>
    /// Captures the specified LDPlayer tab directly and opens the screenshot viewer
    /// with coordinate picking and template crop automatically bound to this tab.
    /// </summary>
    public async Task CaptureTabAndOpenViewerAsync(LdInstanceInfo inst)
    {
        if (inst == null) return;

        if (!inst.IsRunning)
        {
            MessageBox.Show(this,
                $"Tab #{inst.Index} ({inst.Name}) hiện đang tắt.\nVui lòng khởi động tab trước khi chụp màn hình.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            EmulatorManager.SelectedInstance = inst;
            inst.OperationStatus = "Đang chụp màn hình...";

            var capture = await LdPlayerService.CaptureInstanceAsync(inst, EmulatorManager.CurrentPath);
            inst.OperationStatus = "Đã chụp màn hình";

            var targetStep = GetCurrentlyTargetedStep();
            ViewerMode initialMode = ViewerMode.PickPoint;
            if (targetStep != null && (targetStep.Type is "find_image" or "click_image" or "wait_for_image" or "image_exists"))
            {
                initialMode = ViewerMode.SelectRegion;
            }

            if (_activeViewer != null && _activeViewer.IsLoaded && _activeViewer.IsVisible)
            {
                // Cập nhật ảnh vào cửa sổ đang mở mà không tự đóng hay mở thêm cửa sổ mới
                _activeViewer.UpdateImage(capture.Path, capture, inst);
                _activeViewer.SelectMode(initialMode);
                if (_activeViewer.WindowState == WindowState.Minimized)
                    _activeViewer.WindowState = WindowState.Normal;
                _activeViewer.Activate();
            }
            else
            {
                var viewer = new ImageViewerWindow(
                    capture.Path,
                    capture,
                    inst,
                    mode: initialMode,
                    title: $"macro - Chụp màn hình Tab #{inst.Index} ({inst.Name})");
                viewer.Owner = this;

                _activeViewer = viewer;
                viewer.Closed += (_, _) =>
                {
                    if (_activeViewer == viewer)
                        _activeViewer = null;
                };
                viewer.Show();
            }
        }
        catch (Exception ex)
        {
            inst.OperationStatus = "Chụp ảnh thất bại";
            MessageBox.Show(this,
                $"Không thể chụp màn hình tab #{inst.Index} ({inst.Name}):\n\n{ex.Message}",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

