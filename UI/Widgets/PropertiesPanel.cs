using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Macro.Core;
using Macro.Core.Emulator;
using Macro.Core.Screen;
using Macro.Core.Services;
using Macro.Localization;
using Macro.UI.Dialogs;
using ExecutionContext = Macro.Core.ExecutionContext;

namespace Macro.UI.Widgets;

/// <summary>
/// Right panel: settings of the selected step. The form is generated from
/// the action's ParamSchema, so new action types get an editor for free.
/// Changes apply to the model immediately. The "Test" button runs the
/// selected action for real through its ActionDefinition.Execute - the
/// same code path the execution engine uses.
/// </summary>
public class PropertiesPanel : UserControl
{
    public event Action<ActionStep> Changed;        // a param was edited
    public event Action<ActionStep> AddToWorkflowRequested; // add draft step to workflow
    public Func<Workflow> WorkflowProvider { get; set; }
    /// <summary>Test/log sink wired to the Output panel by MainWindow.</summary>
    public Action<string> OutputSink;

    private ActionStep _step;
    private ActionDefinition _def;
    private bool _building;
    private bool _testing;
    private bool _isDraft;

    public bool IsDraft => _isDraft;
    public ActionStep CurrentStep => _step;

    private readonly StackPanel _form;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly Image _titleIcon;
    private readonly Button _btnAdd;
    private readonly Button _testButton;
    private readonly TextBlock _testStatus;
    private TextBox _imageParamBox;
    private Action _reloadPreviewAction;

    public PropertiesPanel()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ---- header: icon + title + description + [Add] + [Test] buttons
        var header = new Grid { Margin = new Thickness(10, 8, 10, 3) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleIcon = new Image
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        Grid.SetColumn(_titleIcon, 0);
        header.Children.Add(_titleIcon);

        _title = Ui.Label("No step selected", T.Text, 13, true);
        _description = new TextBlock
        {
            Foreground = T.Muted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
        };
        var textStack = new StackPanel();
        textStack.Children.Add(_title);
        textStack.Children.Add(_description);
        Grid.SetColumn(textStack, 1);
        header.Children.Add(textStack);

        var actionsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _btnAdd = new Button
        {
            Content = Loc.T("prop.add", "Thêm"),
            Padding = new Thickness(12, 3, 12, 3),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Background = (Brush)new BrushConverter().ConvertFromString("#38B26C"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("prop.add.tip", "Thêm thao tác này cùng các thông số hiện tại vào macro"),
            Margin = new Thickness(0, 0, 6, 0),
            Visibility = Visibility.Collapsed,
        };
        _btnAdd.Click += (_, _) =>
        {
            if (_step != null && _isDraft)
                AddToWorkflowRequested?.Invoke(_step);
        };
        actionsStack.Children.Add(_btnAdd);

        _testButton = new Button
        {
            Content = Loc.T("prop.test", "\u25B6 Test"),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            ToolTip = Loc.T("prop.test.tip",
                "Run this action now with current parameters"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _testButton.Click += (_, _) => RunTest();
        actionsStack.Children.Add(_testButton);

        Grid.SetColumn(actionsStack, 2);
        header.Children.Add(actionsStack);
        root.Children.Add(header);
        Grid.SetRow(header, 0);

        var testRow = new Grid { Margin = new Thickness(10, 1, 10, 2) };
        _testStatus = Ui.Label("", T.Muted, 11);
        testRow.Children.Add(_testStatus);
        root.Children.Add(testRow);
        Grid.SetRow(testRow, 1);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(6, 2, 4, 2),
        };
        _form = new StackPanel { Margin = new Thickness(2, 0, 4, 0) };
        Grid.SetIsSharedSizeScope(_form, true);
        scroll.Content = _form;
        root.Children.Add(scroll);
        Grid.SetRow(scroll, 2);

        Content = root;
        ShowPlaceholder();
        ActionDefaults.DefaultsChanged += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_step == null || _def == null)
                    ShowPlaceholder();
                else
                    BuildForm();
            });
        };
    }

    // ================================================================ state
    public void SetStep(ActionStep step, bool isDraft = false)
    {
        _isDraft = isDraft;
        _step = step;
        _def = step != null ? ActionRegistry.Get(step.Type) : null;
        if (step == null || _def == null)
        {
            ShowPlaceholder();
            return;
        }
        BuildForm();
    }

    public void RefreshVariablesAndFiles()
    {
        if (_step != null && _def != null)
        {
            BuildForm();
        }
    }

    /// <summary>Re-render all texts after the language changed.</summary>
    public void ApplyLanguage()
    {
        _btnAdd.Content = Loc.T("prop.add", "Thêm");
        _btnAdd.ToolTip = Loc.T("prop.add.tip",
            "Thêm thao tác này cùng các thông số hiện tại vào macro");
        _testButton.Content = Loc.T("prop.test", "\u25B6 Test");
        _testButton.ToolTip = Loc.T("prop.test.tip",
            "Run this action now with current parameters");
        if (_step == null || _def == null) ShowPlaceholder();
        else BuildForm();
    }

    private void ShowPlaceholder()
    {
        _imageParamBox = null;
        _reloadPreviewAction = null;
        _titleIcon.Source = null;
        _title.Text = Loc.T("prop.none", "Chưa chọn thao tác");
        _description.Text = Loc.T("prop.none.hint",
            "Chọn một thao tác trong quy trình hoặc thư viện để chỉnh sửa thuộc tính.");
        _btnAdd.Visibility = Visibility.Collapsed;
        _testButton.IsEnabled = false;
        _testStatus.Text = "";
        _form.Children.Clear();
    }

    // ================================================================= form
    private void BuildForm()
    {
        _building = true;
        _imageParamBox = null;
        _reloadPreviewAction = null;
        _testStatus.Text = "";
        _form.Children.Clear();

        _titleIcon.Source = Icons.For(_def.Icon, Categories.ColorOf(_def.Category), 16);
        _title.Text = Loc.ActionName(_def.TypeId);
        _description.Text = _isDraft
            ? Loc.T("prop.draft.desc", "Tùy chỉnh thông số rồi nhấn nút 'Thêm' để đưa vào macro.")
            : Loc.ActionDesc(_def.TypeId);
        _btnAdd.Visibility = _isDraft ? Visibility.Visible : Visibility.Collapsed;
        _testButton.IsEnabled = _def.Execute != null && !_testing;
        _testButton.ToolTip = _def.Execute == null
            ? Loc.T("prop.test.container",
                "This block runs inside the workflow - it cannot be tested alone.")
            : Loc.T("prop.test.tip",
                "Run this action now with current parameters");

        // Section header for category
        _form.Children.Add(Ui.SectionHeader(Loc.CategoryName(_def.Category)));

        // ---- common: custom step name
        if (_step.Type == "read_txt_line" && (_step.Name is "Đọc dòng tệp TXT" or "Đọc tệp TXT" or "Đọc dòng" or "Read TXT Line"))
        {
            _step.Name = "";
        }
        var nameBox = new TextBox
        {
            Text = _step.Name,
            Tag = Loc.F("prop.stepname.placeholder", "Default: {0}", _def.Name),
        };
        nameBox.TextChanged += (_, _) =>
        {
            if (!_building)
            {
                _step.Name = nameBox.Text;
                Changed?.Invoke(_step);
            }
        };
        _form.Children.Add(Row(Loc.T("prop.stepname", "Step name"), nameBox, null));

        // ---- parameters from the schema
        bool optionsHeaderAdded = false;
        FrameworkElement foundTargetRow = null;
        FrameworkElement notFoundTargetRow = null;
        ComboBox onFoundCombo = null;
        ComboBox notFoundCombo = null;

        FrameworkElement textRow = null;
        FrameworkElement variableNameRow = null;
        ComboBox textSourceCombo = null;

        FrameworkElement attachedFileRow = null;
        FrameworkElement customFileRow = null;
        ComboBox fileSourceCombo = null;

        FrameworkElement packageNameRow = null;
        FrameworkElement appNameRow = null;
        FrameworkElement windowsPathRow = null;
        ComboBox targetTypeCombo = null;

        foreach (var spec in _def.ParamSchema)
        {
            if (!optionsHeaderAdded && IsOptionParameter(spec.Key))
            {
                _form.Children.Add(Ui.SectionHeader(Loc.T("prop.options", "Options")));
                optionsHeaderAdded = true;
            }

            var (control, extra) = BuildControl(spec);
            var row = Row(spec, control, extra);

            if (spec.Key == "found_target_id")
            {
                foundTargetRow = row;
                var onFound = _step.GetStr("on_found", "continue");
                row.Visibility = (onFound is "goto" or "click_and_goto") ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (spec.Key == "not_found_target_id")
            {
                notFoundTargetRow = row;
                var notFound = _step.GetStr("not_found", "fail");
                row.Visibility = (notFound == "goto") ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (_step.Type == "type_text" && spec.Key == "text")
            {
                textRow = row;
                var curVal = _step.GetStr("text_source", "static");
                row.Visibility = (curVal == "variable") ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (_step.Type == "type_text" && spec.Key == "variable_name")
            {
                variableNameRow = row;
                var curVal = _step.GetStr("text_source", "static");
                row.Visibility = (curVal == "variable") ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (_step.Type == "read_txt_line" && spec.Key == "attached_file_id")
            {
                attachedFileRow = row;
                var curVal = _step.GetStr("file_source", "attached");
                row.Visibility = (curVal == "custom") ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (_step.Type == "read_txt_line" && spec.Key == "custom_file_path")
            {
                customFileRow = row;
                var curVal = _step.GetStr("file_source", "attached");
                row.Visibility = (curVal == "custom") ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (_step.Type == "open_application" && spec.Key == "package_name")
            {
                packageNameRow = row;
                var curVal = _step.GetStr("target_type", "emulator");
                row.Visibility = (curVal == "windows") ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (_step.Type == "open_application" && spec.Key == "app_name")
            {
                appNameRow = row;
                var curVal = _step.GetStr("target_type", "emulator");
                row.Visibility = (curVal == "windows") ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (_step.Type == "open_application" && spec.Key == "path")
            {
                windowsPathRow = row;
                var curVal = _step.GetStr("target_type", "emulator");
                row.Visibility = (curVal == "windows") ? Visibility.Visible : Visibility.Collapsed;
            }

            if (spec.Key == "on_found" && control is ComboBox ofc)
                onFoundCombo = ofc;
            else if (spec.Key == "not_found" && control is ComboBox nfc)
                notFoundCombo = nfc;
            else if (spec.Key == "text_source" && control is ComboBox tsc)
                textSourceCombo = tsc;
            else if (spec.Key == "file_source" && control is ComboBox fsc)
                fileSourceCombo = fsc;
            else if (spec.Key == "target_type" && control is ComboBox ttc)
                targetTypeCombo = ttc;

            _form.Children.Add(row);
        }

        if (onFoundCombo != null && foundTargetRow != null)
        {
            onFoundCombo.SelectionChanged += (_, _) =>
            {
                var curVal = _step.GetStr("on_found");
                foundTargetRow.Visibility = (curVal is "goto" or "click_and_goto")
                    ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        if (notFoundCombo != null && notFoundTargetRow != null)
        {
            notFoundCombo.SelectionChanged += (_, _) =>
            {
                var curVal = _step.GetStr("not_found");
                notFoundTargetRow.Visibility = (curVal == "goto")
                    ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        if (textSourceCombo != null)
        {
            textSourceCombo.SelectionChanged += (_, _) =>
            {
                var curVal = _step.GetStr("text_source", "static");
                if (textRow != null) textRow.Visibility = (curVal == "variable") ? Visibility.Collapsed : Visibility.Visible;
                if (variableNameRow != null) variableNameRow.Visibility = (curVal == "variable") ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        if (fileSourceCombo != null)
        {
            fileSourceCombo.SelectionChanged += (_, _) =>
            {
                var curVal = _step.GetStr("file_source", "attached");
                if (attachedFileRow != null) attachedFileRow.Visibility = (curVal == "custom") ? Visibility.Collapsed : Visibility.Visible;
                if (customFileRow != null) customFileRow.Visibility = (curVal == "custom") ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        if (targetTypeCombo != null)
        {
            targetTypeCombo.SelectionChanged += (_, _) =>
            {
                var curVal = _step.GetStr("target_type", "emulator");
                if (packageNameRow != null) packageNameRow.Visibility = (curVal == "windows") ? Visibility.Collapsed : Visibility.Visible;
                if (appNameRow != null) appNameRow.Visibility = (curVal == "windows") ? Visibility.Collapsed : Visibility.Visible;
                if (windowsPathRow != null) windowsPathRow.Visibility = (curVal == "windows") ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        if (_def.IsContainer)
        {
            var thenCount = _step.Children.Count;
            var elseCount = _step.ElseChildren.Count;
            _form.Children.Add(new TextBlock
            {
                Text = Loc.F("prop.container.hint",
                        "This block contains {0} step(s).\nAdd or reorder them in the editor.",
                        thenCount + elseCount)
                    + (elseCount > 0
                        ? $"\nTHEN: {thenCount}  /  ELSE: {elseCount}"
                        : ""),
                Foreground = T.Muted,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 4, 0, 0),
            });
        }

        _form.Children.Add(new TextBlock
        {
            Text = Loc.T("prop.changes", "Changes are applied automatically"),
            Foreground = T.Muted,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4),
        });
        _building = false;
    }

    private static bool IsOptionParameter(string key)
    {
        return key is "timeout_ms" or "not_found" or "not_found_target_id" or "on_found"
                   or "found_target_id" or "use_region" or "auto_save"
                   or "iteration_delay_ms";
    }

    private void Commit(string key, object value)
    {
        if (_building) return;
        _step.Params[key] = value;
        Changed?.Invoke(_step);
    }

    // ============================================================= TEST run
    private async void RunTest()
    {
        if (_step == null || _def?.Execute == null || _testing) return;

        if (!EmulatorManager.IsConfiguredAndValid)
        {
            _testStatus.Text = Loc.T("emu.err.test_blocked", "✘ Cần cấu hình giả lập trước khi thử");
            _testStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F));
            OutputSink?.Invoke("[Test] Bị chặn: Chưa cấu hình trình giả lập Android (LDPlayer) hợp lệ.");
            MessageBox.Show(Window.GetWindow(this),
                Loc.T("emu.err.cannot_run",
                    "Chưa cấu hình trình giả lập Android (LDPlayer) hợp lệ.\n\nVui lòng thiết lập đường dẫn đến LDPlayer trước khi thử thao tác."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            new EmulatorSetupDialog(Window.GetWindow(this)).ShowDialog();
            return;
        }

        _testing = true;
        _testButton.IsEnabled = false;
        _testStatus.Text = Loc.T("prop.testing", "Testing...");
        _testStatus.Foreground = T.Muted;

        var step = _step;
        var def = _def;
        var logs = new List<string>();
        Dictionary<string, object> variables = null;
        string error = null;
        string started = DateTime.Now.ToString("HH:mm:ss");

        // Real execution - identical code path to the workflow engine:
        // ActionDefinition.Execute on an ExecutionContext.
        var wf = WorkflowProvider?.Invoke();
        var ctx = new ExecutionContext(
            CancellationToken.None,
            new ManualResetEventSlim(true),
            msg => logs.Add(msg))
        {
            Workflow = wf,
        };
        try
        {
            var definition = def;
            var target = step;
            await Task.Run(() =>
            {
                if (definition.Validate is { } validate)
                {
                    var validationError = validate(target);
                    if (validationError != null)
                        throw new ActionFailureException(validationError);
                }
                definition.Execute(ctx, target);
                variables = ctx.Variables;
            });
        }
        catch (Exception ex)
        {
            error = ex is ActionFailureException
                ? ex.Message
                : ex.GetType().Name + ": " + ex.Message;
        }

        var typeName = Loc.ActionName(def.TypeId);
        if (error == null)
        {
            _testStatus.Text = Loc.T("prop.test.ok", "\u2714 Test OK")
                               + "  " + started;
            _testStatus.Foreground = new SolidColorBrush(
                Color.FromRgb(0x3F, 0xB9, 0x6F));
            foreach (var line in logs) OutputSink?.Invoke(line);
            OutputSink?.Invoke($"[Test] {typeName} - "
                + Loc.T("prop.test.ok", "Test OK"));
        }
        else
        {
            _testStatus.Text = Loc.T("prop.test.fail", "\u2718 Test failed")
                               + "  " + error;
            _testStatus.Foreground = new SolidColorBrush(
                Color.FromRgb(0xD2, 0x4B, 0x41));
            foreach (var line in logs) OutputSink?.Invoke(line);
            OutputSink?.Invoke($"[Test] {typeName} - "
                + Loc.T("prop.test.fail", "Test failed") + $" - {error}");
        }
        _testing = false;
        _testButton.IsEnabled = true;
    }

    // ------------------------------------------------------------- controls
    private (FrameworkElement control, FrameworkElement extra) BuildControl(
        ParamSpec spec)
    {
        if (spec.Key == "package_name") return (LdAppPackageControl(spec), null);
        if (spec.Key == "attached_file_id") return (AttachedFileSelectorControl(spec), null);
        if (spec.Key == "variable_name") return (VariableNameControl(spec), null);

        switch (spec.Widget)
        {
            case ParamWidget.Int: return (IntControl(spec), null);
            case ParamWidget.Double: return (DoubleControl(spec), null);
            case ParamWidget.Bool: return (BoolControl(spec), null);
            case ParamWidget.Enum: return (EnumControl(spec), null);
            case ParamWidget.MultilineText: return (MultilineControl(spec), null);
            case ParamWidget.Slider: return (SliderControl(spec), null);
            case ParamWidget.Key: return (KeyControl(spec), null);
            case ParamWidget.ImageFile: return (ImageControl(spec), null);
            case ParamWidget.File: return (FileControl(spec), null);
            case ParamWidget.Position: return PositionControl(spec);
            case ParamWidget.Region: return (RegionControl(spec), null);
            case ParamWidget.StepSelector: return (StepSelectorControl(spec), null);
            default: return (TextControl(spec), null);
        }
    }

    private TextBox TextControl(ParamSpec spec)
    {
        var box = new TextBox { Text = _step.GetStr(spec.Key), ToolTip = spec.Tooltip };
        box.TextChanged += (_, _) => Commit(spec.Key, box.Text);
        return box;
    }

    private TextBox MultilineControl(ParamSpec spec)
    {
        var box = new TextBox
        {
            Text = _step.GetStr(spec.Key),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 55,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = spec.Tooltip,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.TextChanged += (_, _) => Commit(spec.Key, box.Text);
        return box;
    }

    private IntegerBox IntControl(ParamSpec spec)
    {
        var min = (int)(spec.Min ?? 0);
        var max = (int)(spec.Max ?? 1000000);
        var box = new IntegerBox(min, max)
        {
            Value = _step.GetInt(spec.Key, (int)(spec.Default ?? 0)),
            ToolTip = spec.Tooltip,
        };
        box.ValueChanged += value => Commit(spec.Key, value);
        return box;
    }

    private TextBox DoubleControl(ParamSpec spec)
    {
        var box = new TextBox
        {
            Text = _step.GetDouble(spec.Key,
                ParamUtil.ToDouble(spec.Default, 0))
                  .ToString("0.##", CultureInfo.InvariantCulture),
            ToolTip = spec.Tooltip,
        };
        box.LostFocus += (_, _) =>
        {
            if (double.TryParse(box.Text, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out var value))
                Commit(spec.Key, Math.Round(value, 3));
        };
        return box;
    }

    private CheckBox BoolControl(ParamSpec spec)
    {
        var label = Loc.ParamLabel(spec.Key, spec.Label);
        var check = new CheckBox
        {
            IsChecked = _step.GetBool(spec.Key),
            ToolTip = spec.Tooltip,
            Content = label,
            Foreground = T.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        check.Checked += (_, _) => Commit(spec.Key, true);
        check.Unchecked += (_, _) => Commit(spec.Key, false);
        return check;
    }

    private ComboBox EnumControl(ParamSpec spec)
    {
        var combo = new ComboBox { ToolTip = spec.Tooltip };
        var values = new List<object>();
        foreach (var (label, value) in spec.Options ?? new())
        {
            // label is a Loc key like "opt.left" - fall back to the raw text
            combo.Items.Add(new ComboBoxItem
            {
                Content = Loc.T(label, label),
            });
            values.Add(value);
        }
        var current = _step.Params.TryGetValue(spec.Key, out var raw)
            ? ParamUtil.ToStringValue(raw) : "";
        var index = values.FindIndex(v =>
            ParamUtil.ToStringValue(v) == current);
        combo.SelectedIndex = index >= 0 ? index : 0;
        combo.SelectionChanged += (_, _) =>
            Commit(spec.Key, values[Math.Max(0, combo.SelectedIndex)]);
        return combo;
    }

    private ComboBox StepSelectorControl(ParamSpec spec)
    {
        var combo = new ComboBox { ToolTip = spec.Tooltip };
        var wf = WorkflowProvider?.Invoke();
        var steps = wf?.GetIndexedSteps(excludeId: _step?.Id) ?? new List<StepDisplayInfo>();

        combo.Items.Add(new ComboBoxItem
        {
            Content = Loc.T("prop.step_selector.none", "-- Chọn lệnh đích --"),
            Tag = "",
        });

        var currentTargetId = _step?.GetStr(spec.Key) ?? "";
        int selectedIndex = 0;
        bool foundCurrent = false;

        for (int i = 0; i < steps.Count; i++)
        {
            var item = steps[i];
            var cbi = new ComboBoxItem
            {
                Content = item.DisplayText,
                Tag = item.Id,
            };
            combo.Items.Add(cbi);
            if (!string.IsNullOrEmpty(currentTargetId) && item.Id == currentTargetId)
            {
                selectedIndex = combo.Items.Count - 1;
                foundCurrent = true;
            }
        }

        if (!string.IsNullOrEmpty(currentTargetId) && !foundCurrent)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = Loc.F("prop.step_selector.deleted", "[Lệnh đã bị xóa: {0}]", currentTargetId),
                Tag = currentTargetId,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#D24B41"),
            });
            selectedIndex = combo.Items.Count - 1;
        }

        combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string id)
            {
                Commit(spec.Key, id);
            }
        };
        return combo;
    }

    private FrameworkElement AttachedFileSelectorControl(ParamSpec spec)
    {
        var panel = new StackPanel();
        var combo = new ComboBox { ToolTip = spec.Tooltip };

        var wf = WorkflowProvider?.Invoke();
        var files = wf?.AttachedFiles ?? new List<AttachedTxtFile>();

        var currentId = _step?.GetStr(spec.Key) ?? "";

        combo.Items.Add(new ComboBoxItem
        {
            Content = Loc.T("prop.attached.select", "-- Chọn tệp TXT đính kèm --"),
            Tag = "",
        });

        int selectedIndex = 0;
        bool foundCurrent = false;

        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var cbi = new ComboBoxItem
            {
                Content = $"📄 {f.Name}",
                Tag = f.Id,
            };
            combo.Items.Add(cbi);
            if (!string.IsNullOrEmpty(currentId) && (f.Id == currentId || f.Name == currentId))
            {
                selectedIndex = combo.Items.Count - 1;
                foundCurrent = true;
            }
        }

        if (!string.IsNullOrEmpty(currentId) && !foundCurrent)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"⚠ {currentId} (Không tìm thấy tệp)",
                Tag = currentId,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#D24B41"),
            });
            selectedIndex = combo.Items.Count - 1;
        }

        if (files.Count == 1 && string.IsNullOrEmpty(currentId))
        {
            selectedIndex = 1;
            Commit(spec.Key, files[0].Id);
        }

        combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item)
            {
                var val = item.Tag?.ToString() ?? "";
                Commit(spec.Key, val);
            }
        };

        var btnManage = new Button
        {
            Content = "📄 " + Loc.T("prop.attached.manage", "Quản lý tệp TXT đính kèm..."),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Mở cửa sổ đính kèm thêm hoặc xem trước các tệp TXT riêng của macro này",
        };
        btnManage.Click += (_, _) =>
        {
            if (wf != null)
            {
                var win = Window.GetWindow(this);
                var dlg = new AttachedFilesDialog(wf, win, null, () =>
                {
                    BuildForm();
                });
                dlg.ShowDialog();
            }
        };

        panel.Children.Add(combo);
        panel.Children.Add(btnManage);
        return panel;
    }

    private FrameworkElement VariableNameControl(ParamSpec spec)
    {
        var combo = new ComboBox
        {
            IsEditable = true,
            ToolTip = spec.Tooltip ?? "Chọn hoặc nhập tên biến",
        };

        var currentVal = _step?.GetStr(spec.Key) ?? "var1";
        combo.Text = currentVal;

        var wf = WorkflowProvider?.Invoke();
        var discoveredVars = wf?.GetDiscoveredVariables() ?? new List<string>();

        var defaultVars = new[] { "var1", "var2", "account", "username", "password", "line" };
        var allVars = new HashSet<string>(discoveredVars, StringComparer.OrdinalIgnoreCase);
        foreach (var dv in defaultVars) allVars.Add(dv);

        foreach (var v in allVars)
        {
            combo.Items.Add(v);
        }

        combo.Text = currentVal;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string strVal)
            {
                combo.Text = strVal;
                Commit(spec.Key, strVal);
            }
        };

        void OnTextCommit()
        {
            var txt = combo.Text.Trim();
            if (txt.Length > 0) Commit(spec.Key, txt);
        }

        combo.LostFocus += (_, _) => OnTextCommit();
        combo.KeyUp += (_, _) => OnTextCommit();

        return combo;
    }

    private Grid SliderControl(ParamSpec spec)
    {
        double lo = spec.Min ?? 0;
        double hi = spec.Max ?? 1;
        var current = _step.GetDouble(spec.Key, ParamUtil.ToDouble(spec.Default, lo));

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1000,
            Value = (current - lo) / (hi - lo) * 1000,
            ToolTip = spec.Tooltip,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var valueLabel = Ui.Label(current.ToString("0.00"), T.Text, 11);
        valueLabel.MinWidth = 32;
        valueLabel.Margin = new Thickness(6, 0, 0, 0);
        valueLabel.VerticalAlignment = VerticalAlignment.Center;

        void CommitSlider()
        {
            var value = Math.Round(lo + (hi - lo) * slider.Value / 1000.0, 3);
            valueLabel.Text = value.ToString("0.00");
            Commit(spec.Key, value);
        }
        slider.ValueChanged += (_, _) => CommitSlider();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueLabel, 1);
        grid.Children.Add(slider);
        grid.Children.Add(valueLabel);
        return grid;
    }

    private Grid KeyControl(ParamSpec spec)
    {
        var box = new TextBox
        {
            Text = _step.GetStr(spec.Key),
            ToolTip = "Key name, e.g. enter, f5, a  -  or use Capture",
            MinWidth = 50,
        };
        var capture = new Button
        {
            Content = Loc.T("prop.capturekey", "Capture..."),
            Padding = new Thickness(6, 2.5, 6, 2.5),
            Margin = new Thickness(4, 0, 0, 0),
        };
        capture.Click += (_, _) =>
        {
            var dialog = new KeyCaptureDialog(box.Text)
            {
                Owner = Window.GetWindow(this),
            };
            dialog.ShowDialog();
            if (dialog.KeyName is { Length: > 0 } name)
                box.Text = name;
        };
        box.TextChanged += (_, _) => Commit(spec.Key, box.Text);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0);
        Grid.SetColumn(capture, 1);
        grid.Children.Add(box);
        grid.Children.Add(capture);
        return grid;
    }

    private Grid FileControl(ParamSpec spec)
    {
        var box = new TextBox { Text = _step.GetStr(spec.Key), MinWidth = 50 };
        var browse = new Button
        {
            Content = Loc.T("prop.browse", "Browse..."),
            Padding = new Thickness(6, 2.5, 6, 2.5),
            Margin = new Thickness(4, 0, 0, 0),
        };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("dlg.choosefile", "Choose a file"),
                Filter = Loc.T("filter.all", "All files (*.*)|*.*"),
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                box.Text = dialog.FileName;
        };
        box.TextChanged += (_, _) => Commit(spec.Key, box.Text);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(box);
        grid.Children.Add(browse);
        return grid;
    }

    private FrameworkElement LdAppPackageControl(ParamSpec spec)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox
        {
            Text = _step.GetStr(spec.Key),
            ToolTip = spec.Tooltip,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.TextChanged += (_, _) => Commit(spec.Key, box.Text);
        Grid.SetColumn(box, 0);
        topGrid.Children.Add(box);

        var btnFetch = new Button
        {
            Content = "📋 " + Loc.T("prop.get_apps", "Lấy danh sách ứng dụng"),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            ToolTip = Loc.T("prop.get_apps.tip", "Lấy danh sách ứng dụng đã cài đặt trên tab LDPlayer đang chọn"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        btnFetch.Click += async (_, _) =>
        {
            int tabIndex = -1;
            if (_step.Params.TryGetValue("tab_index", out var tiObj))
                tabIndex = ParamUtil.ToInt(tiObj, -1);
            if (tabIndex < 0 && EmulatorManager.SelectedInstance != null)
                tabIndex = EmulatorManager.SelectedInstance.Index;

            if (tabIndex < 0)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "Chưa chọn tab giả lập LDPlayer.\n\n"
                    + "• Nguyên nhân: Chưa có tab giả lập nào được chọn hoặc gán cho thao tác này.\n"
                    + "• Cách khắc phục: Hãy tích chọn một tab giả lập trong danh sách 'Tab giả lập LDPlayer' ở bên trái rồi thử lại.",
                    "Chưa chọn tab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!EmulatorManager.IsConfiguredAndValid)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "Chưa cấu hình trình giả lập LDPlayer.\n\n"
                    + "• Nguyên nhân: Đường dẫn đến LDPlayer chưa được thiết lập hoặc không hợp lệ.\n"
                    + "• Cách khắc phục: Vui lòng bấm vào thông báo cấu hình giả lập trên thanh công cụ để chọn thư mục cài đặt LDPlayer.",
                    "Chưa cấu hình giả lập",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var consolePath = EmulatorManager.GetLdConsolePath();
            if (string.IsNullOrEmpty(consolePath) || !File.Exists(consolePath))
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "Không tìm thấy tệp ldconsole.exe.\n\n"
                    + $"• Thư mục hiện tại: {EmulatorManager.CurrentPath}\n"
                    + "• Cách khắc phục: Kiểm tra lại thư mục cài đặt LDPlayer.",
                    "Lỗi tệp công cụ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool isRunning = await LdPlayerService.IsRunningAsync(consolePath, tabIndex);
            if (!isRunning)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Tab #{tabIndex} hiện đang tắt.\n\n"
                    + "• Nguyên nhân: Tab giả lập chưa được khởi động nên không thể kết nối đọc danh sách ứng dụng.\n"
                    + $"• Cách khắc phục: Hãy bấm nút '▶ Mở' tại Tab #{tabIndex} trong danh sách và đợi tab hiển thị trạng thái '🟢 Đang chạy', sau đó bấm lại nút 'Lấy danh sách ứng dụng'.",
                    "Tab chưa khởi động",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            btnFetch.IsEnabled = false;
            btnFetch.Content = "⏳ Đang tải...";

            try
            {
                var (apps, error, remedy) = await LdPlayerService.GetInstalledPackagesAsync(consolePath, tabIndex);
                if (!string.IsNullOrEmpty(error))
                {
                    MessageBox.Show(
                        Window.GetWindow(this),
                        $"{error}\n\n• Cách khắc phục: {remedy}",
                        "Không lấy được danh sách ứng dụng",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (apps == null || apps.Count == 0)
                {
                    MessageBox.Show(
                        Window.GetWindow(this),
                        $"Không tìm thấy ứng dụng nào trên Tab #{tabIndex}.\n\n• Cách khắc phục: Hãy kiểm tra tab giả lập và đảm bảo thiết bị đã khởi động vào màn hình chính Android.",
                        "Danh sách trống",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var inst = EmulatorManager.SelectedInstance ?? LdPlayerService.LiveInstances.GetValueOrDefault(tabIndex);
                var dlg = new AppPickerDialog(apps, box.Text, tabIndex, inst?.Name, Window.GetWindow(this));
                if (dlg.ShowDialog() == true && dlg.SelectedApp != null)
                {
                    box.Text = dlg.SelectedApp.PackageName;
                    Commit("package_name", dlg.SelectedApp.PackageName);
                    Commit("app_name", dlg.SelectedApp.DisplayName);
                    Commit("tab_index", tabIndex);
                    Commit("target_type", "emulator");
                    BuildForm();
                }
            }
            finally
            {
                btnFetch.IsEnabled = true;
                btnFetch.Content = "📋 " + Loc.T("prop.get_apps", "Lấy danh sách ứng dụng");
            }
        };

        Grid.SetColumn(btnFetch, 1);
        topGrid.Children.Add(btnFetch);
        Grid.SetRow(topGrid, 0);
        grid.Children.Add(topGrid);

        var appName = _step.GetStr("app_name");
        if (!string.IsNullOrEmpty(appName))
        {
            var lblName = new TextBlock
            {
                Text = $"Tên ứng dụng: {appName}",
                FontSize = 10.5,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#38B26C"),
                Margin = new Thickness(0, 3, 0, 0),
            };
            Grid.SetRow(lblName, 1);
            grid.Children.Add(lblName);
        }

        return grid;
    }

    private StackPanel ImageControl(ParamSpec spec)
    {
        var panel = new StackPanel();

        var preview = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#1B1C1F"),
            BorderBrush = T.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Height = 68,
            Margin = new Thickness(0, 0, 0, 3),
        };
        var previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(3),
        };
        preview.Child = previewImage;

        void ReloadPreview()
        {
            var raw = _step?.GetStr(spec.Key);
            var path = ScreenshotService.ResolveImagePath(raw);
            try
            {
                if (path is { Length: > 0 } && File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    previewImage.Source = bitmap;
                    return;
                }
            }
            catch { /* fall through to empty */ }
            previewImage.Source = null;
        }

        var fileGrid = FileControl(spec);
        var box = (TextBox)fileGrid.Children[0];
        _imageParamBox = box;
        _reloadPreviewAction = ReloadPreview;

        box.TextChanged += (_, _) => ReloadPreview();

        // "Capture & crop" - screenshot the screen, drag a region in the
        // image viewer, save it as a reusable template for Find Image.
        var cropButton = new Button
        {
            Content = Loc.T("prop.crop", "Capture & crop template..."),
            Padding = new Thickness(6, 2.5, 6, 2.5),
            Margin = new Thickness(0, 3, 0, 0),
            ToolTip = Loc.T("prop.crop.tip",
                "Capture the screen then drag a region to make a template"),
        };
        cropButton.Click += async (_, _) =>
        {
            try
            {
                var inst = EmulatorManager.SelectedInstance;
                if (inst == null || !inst.IsRunning)
                {
                    var live = EmulatorManager.GetLiveInstances().Where(i => i.IsRunning).ToList();
                    if (live.Count == 1)
                    {
                        inst = live[0];
                        EmulatorManager.SelectedInstance = inst;
                    }
                }

                if (inst != null && inst.IsRunning && EmulatorManager.IsConfiguredAndValid)
                {
                    var cap = await LdPlayerService.CaptureInstanceAsync(inst, EmulatorManager.CurrentPath);
                    var viewer = new ImageViewerWindow(cap.Path, cap, targetInstance: inst,
                        mode: ViewerMode.SelectRegion, onTemplateSaved: path =>
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                ApplyImageTemplate(path, inst.Index);
                            });
                        });
                    viewer.Owner = Window.GetWindow(this);
                    viewer.Show();
                }
                else
                {
                    MessageBox.Show(Window.GetWindow(this),
                        "Vui lòng khởi động và chọn một tab giả lập LDPlayer trước khi cắt ảnh mẫu.",
                        Loc.T("dlg.app.title", "macro"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message,
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        panel.Children.Add(preview);
        panel.Children.Add(fileGrid);
        panel.Children.Add(cropButton);

        if (ActionDefaults.HasDefaultImageTemplate)
        {
            var btnUseDefault = new Button
            {
                Content = $"🖼️ {Loc.T("prop.defaults.use_default_image", "Dùng ảnh mặc định")} ({Path.GetFileName(ActionDefaults.DefaultImagePath)})",
                Padding = new Thickness(6, 2.5, 6, 2.5),
                Margin = new Thickness(0, 3, 0, 0),
                ToolTip = "Gán nhanh ảnh mẫu mặc định hiện tại vào thao tác này",
            };
            btnUseDefault.Click += (_, _) =>
            {
                ApplyImageTemplate(ActionDefaults.DefaultImagePath, ActionDefaults.DefaultImageTabIndex);
            };
            panel.Children.Add(btnUseDefault);
        }

        ReloadPreview();
        return panel;
    }

    /// <summary>
    /// Applies an image template path to the current step using the exact same pipeline
    /// as selecting a file via "Chọn tệp...", updating UI textbox, preview image, model parameters,
    /// clearing stale errors, and notifying listeners.
    /// </summary>
    public bool ApplyImageTemplate(string templatePath, int tabIndex = -1)
    {
        if (_step == null) return false;

        string resolvedPath = ScreenshotService.ResolveImagePath(templatePath);
        _step.Params["image"] = resolvedPath;
        if (tabIndex >= 0)
        {
            _step.Params["tab_index"] = tabIndex;
        }

        if (_imageParamBox != null)
        {
            _imageParamBox.Text = resolvedPath;
        }
        else
        {
            BuildForm();
        }

        _reloadPreviewAction?.Invoke();
        _testStatus.Text = "";

        Changed?.Invoke(_step);
        return true;
    }

    private (FrameworkElement control, FrameworkElement extra) PositionControl(
        ParamSpec spec)
    {
        var x = new IntegerBox(-20000, 20000)
        { Value = _step.GetInt("x"), MinWidth = 50, Height = 24 };
        var y = new IntegerBox(-20000, 20000)
        { Value = _step.GetInt("y"), MinWidth = 50, Height = 24 };
        x.ValueChanged += v => Commit("x", v);
        y.ValueChanged += v => Commit("y", v);

        var pick = new Button
        {
            Content = Loc.T("prop.select", "Select..."),
            Padding = new Thickness(6, 2.5, 6, 2.5),
            ToolTip = Loc.T("prop.capture.tip",
                "Click anywhere on the screen"),
        };
        pick.Click += (_, _) =>
        {
            var position = PositionPicker.Pick(Window.GetWindow(this));
            if (position.HasValue)
            {
                x.Value = position.Value.x;
                y.Value = position.Value.y;
            }
        };

        // "Capture & pick" - screenshot, then click a point on the image.
        // Coordinates are converted image->screen via the capture offset.
        var capture = new Button
        {
            Content = Loc.T("prop.capture", "Capture & pick point..."),
            Padding = new Thickness(6, 2.5, 6, 2.5),
            Margin = new Thickness(0, 2, 0, 0),
            ToolTip = Loc.T("prop.capture.tip",
                "Capture the screen then click a point - coordinates fill X/Y"),
        };
        capture.Click += async (_, _) =>
        {
            try
            {
                var inst = EmulatorManager.SelectedInstance;
                if (inst == null || !inst.IsRunning)
                {
                    var live = EmulatorManager.GetLiveInstances().Where(i => i.IsRunning).ToList();
                    if (live.Count == 1)
                    {
                        inst = live[0];
                        EmulatorManager.SelectedInstance = inst;
                    }
                }

                if (inst != null && inst.IsRunning && EmulatorManager.IsConfiguredAndValid)
                {
                    var cap = await LdPlayerService.CaptureInstanceAsync(inst, EmulatorManager.CurrentPath);
                    var viewer = new ImageViewerWindow(cap.Path, cap, targetInstance: inst,
                        mode: ViewerMode.PickPoint, onPicked: (chosenX, chosenY, _, _) =>
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                x.Value = chosenX;
                                y.Value = chosenY;
                            });
                        });
                    viewer.Owner = Window.GetWindow(this);
                    viewer.Show();
                }
                else
                {
                    MessageBox.Show(Window.GetWindow(this),
                        "Vui lòng khởi động và chọn một tab giả lập LDPlayer trước khi chọn tọa độ.",
                        Loc.T("dlg.app.title", "macro"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message,
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void Place(FrameworkElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
        var lblX = Ui.Label("X", T.Muted, 11);
        lblX.Margin = new Thickness(0, 0, 4, 0);
        Place(lblX, 0);
        Place(x, 1);

        var lblY = Ui.Label("Y", T.Muted, 11);
        lblY.Margin = new Thickness(6, 0, 4, 0);
        Place(lblY, 2);
        Place(y, 3);

        Place(pick, 4);
        pick.Margin = new Thickness(5, 0, 0, 0);

        // extra row under the X/Y grid with the capture & pick button
        var extra = new StackPanel { Children = { capture } };

        if (ActionDefaults.HasDefaultCoordinates)
        {
            var tabInfo = ActionDefaults.DefaultTabIndex >= 0 ? $" [Tab #{ActionDefaults.DefaultTabIndex}]" : "";
            var btnUseDefault = new Button
            {
                Content = $"📍 {Loc.T("prop.defaults.use_default_coord", "Dùng tọa độ mặc định")} ({ActionDefaults.DefaultX}, {ActionDefaults.DefaultY}){tabInfo}",
                Padding = new Thickness(6, 2.5, 6, 2.5),
                Margin = new Thickness(0, 3, 0, 0),
                ToolTip = "Gán nhanh tọa độ mặc định hiện tại vào thao tác này",
            };
            btnUseDefault.Click += (_, _) =>
            {
                x.Value = ActionDefaults.DefaultX;
                y.Value = ActionDefaults.DefaultY;
                if (ActionDefaults.DefaultTabIndex >= 0)
                {
                    _step.Params["tab_index"] = ActionDefaults.DefaultTabIndex;
                }
                Commit("x", ActionDefaults.DefaultX);
                Commit("y", ActionDefaults.DefaultY);
            };
            extra.Children.Add(btnUseDefault);
        }

        return (grid, extra);
    }

    private StackPanel RegionControl(ParamSpec spec)
    {
        var panel = new StackPanel();

        var enable = new CheckBox
        {
            Content = Loc.T("param.use_region", "Limit search to a region"),
            IsChecked = _step.GetBool("use_region"),
            Foreground = T.Text,
            Margin = new Thickness(0, 1, 0, 3),
        };
        panel.Children.Add(enable);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var boxes = new Dictionary<string, IntegerBox>();
        var index = 0;
        foreach (var (key, label) in new[]
                 {
                     ("region_x", "X"), ("region_y", "Y"),
                     ("region_w", "W"), ("region_h", "H"),
                 })
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 3, 0) };
            var lbl = Ui.Label(label, T.Muted, 10);
            lbl.Margin = new Thickness(1, 0, 0, 1);
            stack.Children.Add(lbl);
            var box = new IntegerBox(0, 20000)
            { Value = _step.GetInt(key), MinWidth = 35, Height = 22 };
            box.ValueChanged += v => Commit(key, v);
            boxes[key] = box;
            stack.Children.Add(box);
            Grid.SetColumn(stack, index++);
            grid.Children.Add(stack);
        }

        var pick = new Button
        {
            Content = Loc.T("prop.select", "Select..."),
            Padding = new Thickness(6, 2.5, 6, 2.5),
            Margin = new Thickness(3, 11, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            ToolTip = Loc.T("prop.crop.tip",
                "Drag a rectangle on the screen"),
        };
        pick.Click += (_, _) =>
        {
            var region = RegionPicker.Pick(Window.GetWindow(this));
            if (region.HasValue)
            {
                boxes["region_x"].Value = region.Value.x;
                boxes["region_y"].Value = region.Value.y;
                boxes["region_w"].Value = region.Value.w;
                boxes["region_h"].Value = region.Value.h;
                enable.IsChecked = true;
            }
        };
        Grid.SetColumn(pick, 4);
        grid.Children.Add(pick);

        void ApplyEnabled()
        {
            grid.Opacity = enable.IsChecked == true ? 1.0 : 0.45;
            Commit("use_region", enable.IsChecked == true);
        }
        enable.Checked += (_, _) => ApplyEnabled();
        enable.Unchecked += (_, _) => ApplyEnabled();
        ApplyEnabled();
        _building = true;
        panel.Children.Add(grid);
        _building = false;
        return panel;
    }

    // -------------------------------------------------------------- helpers
    private static FrameworkElement Row(ParamSpec spec, FrameworkElement control,
                                        FrameworkElement extra)
    {
        // For BoolWidget (CheckBox), control is self-contained with its own label
        if (spec.Widget == ParamWidget.Bool)
        {
            control.Margin = new Thickness(2, 2, 0, 3);
            if (extra == null) return control;
            var stack = new StackPanel { Margin = new Thickness(0, 1, 0, 3) };
            stack.Children.Add(control);
            stack.Children.Add(extra);
            return stack;
        }

        var labelText = Loc.ParamLabel(spec.Key, spec.Label);
        var unitKey = "param.unit." + spec.Unit;
        var unit = Loc.T(unitKey, spec.Unit);

        // For large or complex widgets that need full width (Position, Region, Image, Multiline)
        if (spec.Widget is ParamWidget.Position or ParamWidget.Region
                        or ParamWidget.ImageFile or ParamWidget.MultilineText)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 1, 0, 3) };
            if (spec.Widget != ParamWidget.Region) // Region already has its checkbox header
            {
                var header = Ui.Label(
                    string.IsNullOrEmpty(unit) ? labelText : $"{labelText} ({unit})",
                    T.Muted, 11);
                header.Margin = new Thickness(1, 0, 0, 2);
                if (spec.Tooltip is { Length: > 0 })
                    ToolTipService.SetToolTip(header, spec.Tooltip);
                stack.Children.Add(header);
            }
            stack.Children.Add(control);
            if (extra != null) stack.Children.Add(extra);
            return stack;
        }

        // For standard inline widgets (Int, Double, Enum, Key, File, Text, Slider):
        // Render as a 2-column compact row with shared label column width
        var row = new Grid { Margin = new Thickness(0, 1, 0, 3) };
        var colLabel = new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "LabelCol",
        };
        var colControl = new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        };
        row.ColumnDefinitions.Add(colLabel);
        row.ColumnDefinitions.Add(colControl);

        var lbl = Ui.Label(labelText, T.Muted, 11);
        lbl.MaxWidth = 110;
        lbl.TextTrimming = TextTrimming.CharacterEllipsis;
        lbl.Margin = new Thickness(1, 0, 8, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        if (spec.Tooltip is { Length: > 0 })
            ToolTipService.SetToolTip(lbl, spec.Tooltip);
        Grid.SetColumn(lbl, 0);
        row.Children.Add(lbl);

        // If there's a unit, place it inline right next to the control
        FrameworkElement controlHost = control;
        if (!string.IsNullOrEmpty(unit))
        {
            var unitGrid = new Grid();
            unitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            unitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(control, 0);
            unitGrid.Children.Add(control);

            var unitLabel = Ui.Label(unit, T.Muted, 11);
            unitLabel.Margin = new Thickness(5, 0, 0, 0);
            unitLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(unitLabel, 1);
            unitGrid.Children.Add(unitLabel);

            controlHost = unitGrid;
        }

        Grid.SetColumn(controlHost, 1);
        row.Children.Add(controlHost);

        if (extra == null) return row;

        var outer = new StackPanel { Margin = new Thickness(0, 1, 0, 3) };
        row.Margin = new Thickness(0, 0, 0, 2);
        outer.Children.Add(row);
        outer.Children.Add(extra);
        return outer;
    }

    private static FrameworkElement Row(string labelText, FrameworkElement control,
                                        string tooltip)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 3) };
        var colLabel = new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "LabelCol",
        };
        var colControl = new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        };
        row.ColumnDefinitions.Add(colLabel);
        row.ColumnDefinitions.Add(colControl);

        var lbl = Ui.Label(labelText, T.Muted, 11);
        lbl.MaxWidth = 110;
        lbl.TextTrimming = TextTrimming.CharacterEllipsis;
        lbl.Margin = new Thickness(1, 0, 8, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        if (tooltip is { Length: > 0 })
            ToolTipService.SetToolTip(lbl, tooltip);
        Grid.SetColumn(lbl, 0);
        row.Children.Add(lbl);

        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }
}
