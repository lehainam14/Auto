using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Macro.Core;
using Macro.Localization;

namespace Macro.UI.Widgets;

/// <summary>
/// Center panel: renders the workflow as a vertical list of step cards.
/// Handles selection, add / duplicate / delete / toggle / reorder, drag &amp;
/// drop (from the library and between cards) and run-state highlighting.
/// </summary>
public class WorkflowEditorPanel : UserControl
{
    public event Action<ActionStep> SelectionChanged;
    public event Action WorkflowChanged;

    private Workflow _workflow;
    private string _selectedId;
    private readonly Dictionary<string, StepCard> _cards = new();
    private readonly StackPanel _list;
    private readonly ScrollViewer _scroll;
    private readonly TextBox _nameBox;
    private readonly TextBlock _count;
    private readonly TextBlock _caption;
    private readonly Button _clearButton;
    private readonly Button _btnTheme;
    private bool _editingEnabled = true;
    private bool _isDarkTheme = true;
    private readonly Grid _rootGrid;
    private readonly Grid _header;

    public bool IsDarkTheme => _isDarkTheme;

    public WorkflowEditorPanel()
    {
        _isDarkTheme = LoadPersistedTheme() == "dark";

        _rootGrid = new Grid();
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ---- header: caption + name + count + theme + clear
        _header = new Grid { Margin = new Thickness(10, 6, 10, 3) };
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // count
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // theme button
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // clear button

        var nameStack = new StackPanel();
        _caption = Ui.Label(Loc.T("editor.caption", "WORKFLOW"), T.Muted, 10.5, true);
        nameStack.Children.Add(_caption);
        _nameBox = new TextBox
        {
            MinWidth = 160,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 1, 6, 0),
            Tag = Loc.T("editor.name.placeholder", "Macro name"),
        };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_workflow != null) _workflow.Name = _nameBox.Text;
            WorkflowChanged?.Invoke();
        };
        nameStack.Children.Add(_nameBox);
        Grid.SetColumn(nameStack, 1);
        _header.Children.Add(nameStack);

        _count = Ui.Label("0", T.Muted, 11);
        _count.Margin = new Thickness(0, 0, 6, 0);
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_count, 2);
        _header.Children.Add(_count);

        _btnTheme = new Button
        {
            Content = _isDarkTheme ? Loc.T("editor.theme.light", "☀️ Sáng") : Loc.T("editor.theme.dark", "🌙 Tối"),
            ToolTip = Loc.T("editor.theme.tip", "Chuyển đổi giao diện Sáng / Tối cho khu vực quy trình"),
            FontSize = 10.5,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.Hand,
        };
        _btnTheme.Click += (_, _) => ToggleTheme();
        Grid.SetColumn(_btnTheme, 3);
        _header.Children.Add(_btnTheme);

        _clearButton = new Button
        {
            Content = Loc.T("editor.clear", "Clear"),
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            ToolTip = Loc.T("editor.clear.tip", "Remove all steps"),
            FontSize = 10.5,
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _clearButton.Click += (_, _) => ClearAll();
        Grid.SetColumn(_clearButton, 4);
        _header.Children.Add(_clearButton);
        _rootGrid.Children.Add(_header);
        Grid.SetRow(_header, 0);

        // ---- scrollable list
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 4, 0),
        };
        _list = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
        _scroll.Content = _list;
        _scroll.AllowDrop = true;
        _scroll.DragOver += OnAreaDragOver;
        _scroll.Drop += OnAreaDrop;
        _rootGrid.Children.Add(_scroll);
        Grid.SetRow(_scroll, 1);

        ApplyThemeStyles();
        Content = _rootGrid;
    }

    public void ToggleTheme()
    {
        _isDarkTheme = !_isDarkTheme;
        SavePersistedTheme(_isDarkTheme ? "dark" : "light");
        ApplyThemeStyles();
        Rebuild();
        if (!string.IsNullOrEmpty(_selectedId))
            SelectStep(_selectedId);
    }

    private void ApplyThemeStyles()
    {
        _btnTheme.Content = _isDarkTheme
            ? Loc.T("editor.theme.light", "☀️ Sáng")
            : Loc.T("editor.theme.dark", "🌙 Tối");
        _btnTheme.ToolTip = Loc.T("editor.theme.tip",
            "Chuyển đổi giao diện Sáng / Tối cho khu vực quy trình");

        if (_isDarkTheme)
        {
            _rootGrid.Background = (Brush)new BrushConverter().ConvertFromString("#191A1D");
            _scroll.Background = (Brush)new BrushConverter().ConvertFromString("#191A1D");
            _caption.Foreground = T.Muted;
            _count.Foreground = T.Muted;
            _nameBox.Background = (Brush)new BrushConverter().ConvertFromString("#26272B");
            _nameBox.Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");
            _nameBox.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#35363B");

            _clearButton.Background = Brushes.Transparent;
            _clearButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");
            _clearButton.BorderBrush = Brushes.Transparent;

            _btnTheme.Background = (Brush)new BrushConverter().ConvertFromString("#2D2F34");
            _btnTheme.Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");
            _btnTheme.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#4A4D55");
        }
        else
        {
            _rootGrid.Background = (Brush)new BrushConverter().ConvertFromString("#F6F8FA");
            _scroll.Background = (Brush)new BrushConverter().ConvertFromString("#F6F8FA");
            _caption.Foreground = (Brush)new BrushConverter().ConvertFromString("#57606A");
            _count.Foreground = (Brush)new BrushConverter().ConvertFromString("#57606A");
            _nameBox.Background = Brushes.White;
            _nameBox.Foreground = (Brush)new BrushConverter().ConvertFromString("#1F2328");
            _nameBox.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D7DE");

            _clearButton.Background = (Brush)new BrushConverter().ConvertFromString("#F3F4F6");
            _clearButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#24292F");
            _clearButton.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D7DE");

            _btnTheme.Background = (Brush)new BrushConverter().ConvertFromString("#F3F4F6");
            _btnTheme.Foreground = (Brush)new BrushConverter().ConvertFromString("#24292F");
            _btnTheme.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D7DE");
        }
    }

    private static string ConfigPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "macro");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    public static string LoadPersistedTheme()
    {
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path)) return "dark";
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("workflow_theme", out var themeElem)
                && themeElem.ValueKind == JsonValueKind.String)
            {
                var val = themeElem.GetString()?.ToLowerInvariant();
                if (val == "light") return "light";
            }
        }
        catch { }
        return "dark";
    }

    public static void SavePersistedTheme(string theme)
    {
        try
        {
            var path = ConfigPath();
            Dictionary<string, object> root = new();
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name != "workflow_theme")
                            root[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }
                catch { }
            }
            root["workflow_theme"] = theme;
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(root, options));
        }
        catch { }
    }

    // ================================================================ model
    public Workflow Workflow => _workflow;

    public void SetWorkflow(Workflow workflow)
    {
        _workflow = workflow;
        _selectedId = null;
        _nameBox.Text = workflow.Name;
        Rebuild();
        SelectionChanged?.Invoke(null);
    }

    public ActionStep SelectedStep() =>
        _workflow?.Find(_selectedId);

    public void SelectStep(string stepId)
    {
        _selectedId = stepId;
        foreach (var pair in _cards)
            pair.Value.SetSelected(pair.Key == stepId);
        SelectionChanged?.Invoke(SelectedStep());
    }

    public void SetEditingEnabled(bool enabled)
    {
        _editingEnabled = enabled;
        _nameBox.IsEnabled = enabled;
        _clearButton.IsEnabled = enabled;
        foreach (var card in _cards.Values)
            card.SetInteractive(enabled);
    }

    /// <summary>Re-render all texts after the language changed.</summary>
    public void ApplyLanguage()
    {
        _caption.Text = Loc.T("editor.caption", "WORKFLOW");
        _nameBox.Tag = Loc.T("editor.name.placeholder", "Macro name");
        _clearButton.Content = Loc.T("editor.clear", "Clear");
        _clearButton.ToolTip = Loc.T("editor.clear.tip", "Remove all steps");
        _btnTheme.Content = _isDarkTheme
            ? Loc.T("editor.theme.light", "☀️ Sáng")
            : Loc.T("editor.theme.dark", "🌙 Tối");
        _btnTheme.ToolTip = Loc.T("editor.theme.tip",
            "Chuyển đổi giao diện Sáng / Tối cho khu vực quy trình");
        Rebuild();
        // selection is restored inside Rebuild via _selectedId
    }

    // ======================================================== edit actions
    public void AddStep(string typeId, string beforeId = null,
                        string afterId = null, string intoId = null,
                        bool intoElse = false)
    {
        if (_workflow == null || !_editingEnabled) return;
        var step = ActionRegistry.CreateStep(typeId);
        if (step == null) return;
        InsertStep(step, beforeId, afterId, intoId, intoElse);
    }

    public void AddStep(ActionStep step, string beforeId = null,
                        string afterId = null, string intoId = null,
                        bool intoElse = false)
    {
        if (_workflow == null || !_editingEnabled || step == null) return;
        InsertStep(step, beforeId, afterId, intoId, intoElse);
    }

    private void InsertStep(ActionStep step, string beforeId,
                            string afterId, string intoId, bool intoElse)
    {
        if (intoId != null && _workflow.Find(intoId) is { } container)
        {
            if (intoElse) container.ElseChildren.Add(step);
            else container.Children.Add(step);
        }
        else if (beforeId != null && _workflow.ParentList(beforeId) is { } beforeList)
        {
            beforeList.Insert(beforeList.FindIndex(s => s.Id == beforeId), step);
        }
        else if (afterId != null && _workflow.ParentList(afterId) is { } afterList)
        {
            afterList.Insert(afterList.FindIndex(s => s.Id == afterId) + 1, step);
        }
        else
        {
            _workflow.Actions.Add(step);
        }
        Rebuild();
        SelectStep(step.Id);
        WorkflowChanged?.Invoke();
    }

    public void DuplicateStep(string stepId)
    {
        var step = _workflow?.Find(stepId);
        if (step == null) return;
        var list = _workflow.ParentList(stepId);
        list.Insert(list.IndexOf(step) + 1, step.DeepCloneWithNewIds());
        Rebuild();
        WorkflowChanged?.Invoke();
    }

    public void DeleteStep(string stepId)
    {
        var step = _workflow?.Find(stepId);
        if (step == null) return;
        if (step.Children.Count > 0 || step.ElseChildren.Count > 0)
        {
            var def = ActionRegistry.Get(step.Type);
            var label = step.Name is { Length: > 0 } ? step.Name
                : Loc.ActionName(step.Type);
            var answer = MessageBox.Show(
                Loc.F("dlg.delete.text",
                    "'{0}' contains {1} step(s). Delete them all?",
                    label, step.Children.Count + step.ElseChildren.Count),
                Loc.T("dlg.delete.title", "Delete block"),
                MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }
        _workflow.ParentList(stepId).Remove(step);
        if (_selectedId == stepId) SelectStep(null);
        Rebuild();
        WorkflowChanged?.Invoke();
    }

    public void ToggleStep(string stepId)
    {
        var step = _workflow?.Find(stepId);
        if (step == null) return;
        step.Enabled = !step.Enabled;
        Rebuild();      // keeps number/summary in sync for nested steps too
        WorkflowChanged?.Invoke();
    }

    public void MoveStep(string stepId, int delta)
    {
        var list = _workflow?.ParentList(stepId);
        if (list == null) return;
        var index = list.FindIndex(s => s.Id == stepId);
        var target = index + delta;
        if (target < 0 || target >= list.Count) return;
        var step = list[index];
        list.RemoveAt(index);
        list.Insert(target, step);
        Rebuild();
        SelectStep(stepId);
        WorkflowChanged?.Invoke();
    }

    /// <summary>Move a step so it lands right before the target step.</summary>
    public void MoveStepBefore(string sourceId, string targetId)
    {
        if (_workflow == null || sourceId == targetId) return;
        if (_workflow.IsDescendant(targetId, sourceId)) return;   // into own children
        var source = _workflow.Find(sourceId);
        var sourceList = _workflow.ParentList(sourceId);
        if (source == null || sourceList == null) return;
        sourceList.Remove(source);
        var targetList = _workflow.ParentList(targetId);
        targetList.Insert(targetList.FindIndex(s => s.Id == targetId), source);
        Rebuild();
        SelectStep(sourceId);
        WorkflowChanged?.Invoke();
    }

    /// <summary>Move a step into the last position of a container.</summary>
    public void MoveStepInto(string sourceId, string containerId,
                             bool elseBranch = false)
    {
        if (_workflow == null || sourceId == containerId) return;
        if (_workflow.IsDescendant(containerId, sourceId)) return;
        var source = _workflow.Find(sourceId);
        var sourceList = _workflow.ParentList(sourceId);
        if (source == null || sourceList == null) return;
        sourceList.Remove(source);
        var container = _workflow.Find(containerId);
        if (elseBranch) container.ElseChildren.Add(source);
        else container.Children.Add(source);
        Rebuild();
        SelectStep(sourceId);
        WorkflowChanged?.Invoke();
    }

    private void ClearAll()
    {
        if (_workflow == null || _workflow.Actions.Count == 0) return;
        var answer = MessageBox.Show(
            Loc.T("dlg.clear.text", "Remove all steps from the workflow?"),
            Loc.T("dlg.clear.title", "Clear workflow"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _workflow.Actions.Clear();
        SelectStep(null);
        Rebuild();
        WorkflowChanged?.Invoke();
    }

    // ============================================================ run state
    public void ClearRunState()
    {
        foreach (var card in _cards.Values)
        {
            card.SetRunning(false);
            card.SetDone(false);
        }
    }

    public void HighlightRunning(string stepId)
    {
        foreach (var pair in _cards)
            pair.Value.SetRunning(pair.Key == stepId);
        if (_cards.TryGetValue(stepId, out var card))
            card.BringIntoView();
    }

    public void MarkDone(string stepId)
    {
        if (_cards.TryGetValue(stepId, out var card)) card.SetDone(true);
    }

    /// <summary>Refresh one card after its properties changed.</summary>
    public void RefreshCard(string stepId)
    {
        if (_cards.TryGetValue(stepId, out var card)) card.RefreshContent();
    }

    // =============================================================== render
    public void Rebuild()
    {
        _cards.Clear();
        _list.Children.Clear();

        if (_workflow == null || _workflow.Actions.Count == 0)
        {
            _list.Children.Add(BuildEmptyState());
            if (_workflow != null) _count.Text = Loc.T("editor.count.1", "1 step").Replace("1", "0");
            return;
        }

        BuildRows(_workflow.Actions, _list, "", null);
        _list.Children.Add(BuildAddStepButton(null));

        int count = 0;
        void CountRows(List<ActionStep> steps)
        {
            foreach (var step in steps)
            {
                count++;
                CountRows(step.Children);
                CountRows(step.ElseChildren);
            }
        }
        CountRows(_workflow.Actions);
        _count.Text = count == 1
            ? Loc.T("editor.count.1", "1 step")
            : Loc.F("editor.count.n", "{0} steps", count);
    }

    private void BuildRows(List<ActionStep> steps, StackPanel host,
                           string prefix, StackPanel elseHost = null)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var def = ActionRegistry.Get(step.Type);
            if (def == null) continue;
            var number = $"{prefix}{index + 1}";

            var card = new StepCard(step, def, number, _isDarkTheme, _workflow)
            {
                Margin = new Thickness(prefix.Length > 0 ? 18 : 0, 0, 0, 0),
            };
            card.Selected += SelectStep;
            card.AddRequested += () => AddStepMenu.Show(card, Window.GetWindow(this),
                typeId => AddStep(typeId, intoId: step.Id));
            card.Toggled += () => ToggleStep(step.Id);
            card.Duplicated += () => DuplicateStep(step.Id);
            card.Deleted += () => DeleteStep(step.Id);
            card.MovedUp += () => MoveStep(step.Id, -1);
            card.MovedDown += () => MoveStep(step.Id, +1);
            card.SetSelected(step.Id == _selectedId);
            card.DropNewRequested += (typeId, beforeId) =>
                AddStep(typeId, beforeId: beforeId);
            card.DropMoveRequested += MoveStepBefore;
            _cards[step.Id] = card;

            host.Children.Add(BuildInsertRow(step.Id, prefix.Length > 0 ? 18 : 0));
            host.Children.Add(card);

            if (def.IsContainer)
                host.Children.Add(BuildContainerBody(step, number, elseHost));
        }
        // trailing insert row inside a container body
        if (prefix.Length > 0)
        {
            var containerId = host.Tag as string;
            var isElse = elseHost != null && ReferenceEquals(host, elseHost);
            host.Children.Add(BuildInsertRow(null, 18,
                intoId: containerId, intoElse: isElse));
        }
    }

    /// <summary>Container body: dashed frame with the child rows. For the
    /// If/Else action it renders two labeled sections (THEN / ELSE) that
    /// each accept drops and have their own insert rows.</summary>
    private FrameworkElement BuildContainerBody(ActionStep step, string number,
                                                StackPanel elseHost = null)
    {
        var def = ActionRegistry.Get(step.Type);
        var isIfElse = def is { TypeId: "if_else" };

        var body = new Border
        {
            Style = (Style)Application.Current.Resources["LoopBody"],
            Background = _isDarkTheme
                ? (Brush)new BrushConverter().ConvertFromString("#1F2024")
                : (Brush)new BrushConverter().ConvertFromString("#EDF1F7"),
            BorderBrush = _isDarkTheme
                ? (Brush)new BrushConverter().ConvertFromString("#35363B")
                : (Brush)new BrushConverter().ConvertFromString("#C6CBD1"),
            Margin = new Thickness(18, 0, 0, 4),
            Padding = new Thickness(4),
            AllowDrop = true,
            Tag = step.Id,
        };

        if (isIfElse)
        {
            var outer = new StackPanel();

            var thenHost = new StackPanel { Tag = step.Id };
            outer.Children.Add(BuildBranchHeader(Loc.T("editor.then",
                "THEN (true)"), _isDarkTheme ? "#3FB96F" : "#1A7F37"));
            if (step.Children.Count > 0)
                BuildRows(step.Children, thenHost, number + ".", null);
            else
                outer.Children.Add(EmptyBranchLabel());
            outer.Children.Add(thenHost);

            outer.Children.Add(BuildBranchHeader(Loc.T("editor.else",
                "ELSE (false)"), _isDarkTheme ? "#F0764F" : "#CF222E"));
            var elsePanel = new StackPanel { Tag = step.Id };
            if (step.ElseChildren.Count > 0)
                BuildRows(step.ElseChildren, elsePanel, number + "E.", elsePanel);
            else
                outer.Children.Add(EmptyBranchLabel());
            outer.Children.Add(elsePanel);

            body.Child = outer;
            WireContainerDrops(body, step.Id, elsePanel);
            return body;
        }

        var rows = new StackPanel { Tag = step.Id };
        if (step.Children.Count > 0)
            BuildRows(step.Children, rows, number + ".");
        else
            rows.Children.Add(new TextBlock
            {
                Text = Loc.T("editor.emptyblock",
                    "Empty block - add steps inside"),
                Foreground = _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#57606A"),
                FontSize = 12,
                Margin = new Thickness(10, 4, 0, 4),
            });
        body.Child = rows;
        WireContainerDrops(body, step.Id, null);
        return body;
    }

    private TextBlock BuildBranchHeader(string text, string color)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = (Brush)new BrushConverter().ConvertFromString(color),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(6, 4, 0, 2),
        };
    }

    private TextBlock EmptyBranchLabel() => new()
    {
        Text = Loc.T("editor.emptyblock", "Empty block - add steps inside"),
        Foreground = _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#57606A"),
        FontSize = 12,
        Margin = new Thickness(10, 2, 0, 4),
    };

    private void WireContainerDrops(Border body, string stepId,
                                    StackPanel elsePanel)
    {
        body.DragOver += (_, e) =>
        {
            if (_editingEnabled && (e.Data.GetDataPresent("macro-action-type")
                || e.Data.GetDataPresent("macro-step-id")))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        body.Drop += (_, e) =>
        {
            if (!_editingEnabled) return;
            if (e.Data.GetData("macro-action-type") is string typeId)
                AddStep(typeId, intoId: stepId,
                        intoElse: elsePanel != null && IsPointInRight(e, elsePanel));
            else if (e.Data.GetData("macro-step-id") is string sourceId)
            {
                if (elsePanel != null && IsPointInRight(e, elsePanel))
                    MoveStepInto(sourceId, stepId, elseBranch: true);
                else
                    MoveStepInto(sourceId, stepId);
            }
            e.Handled = true;
        };
    }

    private static bool IsPointInRight(DragEventArgs e, UIElement panel)
    {
        var position = e.GetPosition(panel);
        return position.X >= 0 && position.Y >= 0;
    }

    private Button BuildInsertRow(string beforeId, int indent,
                                  string intoId = null, bool intoElse = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Btn.Insert"],
            Content = "+",
            ToolTip = Loc.T("editor.insert.tip", "Insert a step here"),
            Margin = new Thickness(indent, 0, 0, 0),
            Tag = (beforeId, intoId, intoElse),
        };
        button.Click += (_, _) =>
        {
            var (before, into, isElse) = ((string, string, bool))button.Tag;
            AddStepMenu.Show(button, Window.GetWindow(this), typeId =>
                AddStep(typeId,
                        beforeId: into == null ? before : null,
                        intoId: into,
                        intoElse: isElse));
        };
        return button;
    }

    private Button BuildAddStepButton(string beforeId)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Btn.AddStep"],
            Content = Ui.Label(Loc.T("editor.add", "\uFF0B  Add step"),
                               _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#57606A"), 11.5),
            Padding = new Thickness(5),
            Margin = new Thickness(0, 2, 0, 2),
            ToolTip = Loc.T("editor.add.tip", "Append a new step at the end"),
        };
        if (!_isDarkTheme)
        {
            button.Background = Brushes.White;
            button.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D7DE");
        }
        button.Click += (_, _) =>
        {
            AddStepMenu.Show(button, Window.GetWindow(this),
                typeId => AddStep(typeId, beforeId: beforeId));
        };
        return button;
    }

    private Border BuildEmptyState()
    {
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(new TextBlock
        {
            Text = "\uFF0B",
            FontSize = 34,
            Foreground = _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#8C959F"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("editor.empty.title", "No steps yet"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = _isDarkTheme ? T.Text : (Brush)new BrushConverter().ConvertFromString("#1F2328"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("editor.empty.hint",
                "Double-click an action in the library on the left,\n"
                + "or drag one into this area."),
            Foreground = _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#57606A"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 14),
        });
        var add = new Button
        {
            Style = (Style)Application.Current.Resources["Btn.AddStep"],
            Content = Ui.Label(Loc.T("editor.addfirst",
                              "\uFF0B  Add first step"),
                              _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#24292F"), 13),
            Padding = new Thickness(16, 8, 16, 8),
        };
        if (!_isDarkTheme)
        {
            add.Background = (Brush)new BrushConverter().ConvertFromString("#F6F8FA");
            add.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D7DE");
        }
        add.Click += (_, _) => AddStepMenu.Show(add, Window.GetWindow(this),
            typeId => AddStep(typeId));
        stack.Children.Add(add);

        return new Border
        {
            BorderBrush = _isDarkTheme ? T.BorderHi : (Brush)new BrushConverter().ConvertFromString("#D0D7DE"),
            Background = _isDarkTheme ? Brushes.Transparent : Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(24, 30, 24, 10),
            Child = stack,
        };
    }

    // ------------------------------------------------- drop on empty space
    private void OnAreaDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _editingEnabled
            && (e.Data.GetDataPresent("macro-action-type")
                || e.Data.GetDataPresent("macro-step-id"))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnAreaDrop(object sender, DragEventArgs e)
    {
        if (!_editingEnabled) return;
        if (e.Data.GetData("macro-action-type") is string typeId)
            AddStep(typeId);
        else if (e.Data.GetData("macro-step-id") is string sourceId)
            MoveStep(sourceId, 1_000_000);      // to the very end
        e.Handled = true;
    }
}

/// <summary>One workflow step rendered as a block/card.</summary>
public class StepCard : Border
{
    public event Action<string> Selected;      // step id
    public event Action Toggled;
    public event Action Duplicated;
    public event Action Deleted;
    public event Action MovedUp;
    public event Action MovedDown;
    public event Action AddRequested;          // container: add child

    private readonly ActionStep _step;
    private readonly ActionDefinition _def;
    private readonly bool _isDarkTheme;
    private readonly Workflow _workflow;
    private TextBlock _titleLabel;
    private TextBlock _summaryLabel;
    private bool _selected;
    private bool _running;
    private bool _done;
    private bool _interactive = true;

    public StepCard(ActionStep step, ActionDefinition def, string number, bool isDarkTheme = true, Workflow workflow = null)
    {
        _step = step;
        _def = def;
        _isDarkTheme = isDarkTheme;
        _workflow = workflow;

        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6, 3.5, 6, 3.5);
        Margin = new Thickness(0, 0, 0, 2);
        AllowDrop = true;
        Cursor = Cursors.Hand;
        ToolTip = def.Description;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // grip
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // number
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // icon
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // buttons

        var grip = new TextBlock
        {
            Text = "\u2261",                       // ≡
            Foreground = _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#8C959F"),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.SizeAll,
            ToolTip = Loc.T("editor.grip.tip", "Drag to move this step"),
        };
        grip.MouseLeftButtonDown += OnGripDown;
        Grid.SetColumn(grip, 0);
        row.Children.Add(grip);

        var displayNum = number.StartsWith("#") ? number : $"#{number}";
        var numberBox = new Border
        {
            Background = _isDarkTheme
                ? (Brush)new BrushConverter().ConvertFromString("#26272B")
                : (Brush)new BrushConverter().ConvertFromString("#EAEEF2"),
            BorderBrush = _isDarkTheme
                ? (Brush)new BrushConverter().ConvertFromString("#35363B")
                : (Brush)new BrushConverter().ConvertFromString("#D0D7DE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Label(displayNum,
                _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#24292F"),
                10.5, true),
        };
        Grid.SetColumn(numberBox, 1);
        row.Children.Add(numberBox);

        var icon = Ui.IconImage(def.Icon, Categories.ColorOf(def.Category), 15);
        icon.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(icon, 2);
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var isOldTxtName = step.Type == "read_txt_line" && (step.Name is "Đọc dòng tệp TXT" or "Đọc tệp TXT" or "Đọc dòng" or "Read TXT Line");
        var title = Ui.Label(
            (step.Name is { Length: > 0 } && !isOldTxtName) ? step.Name : Loc.ActionName(def.TypeId),
            _isDarkTheme ? T.Text : (Brush)new BrushConverter().ConvertFromString("#1F2328"), 12, true);
        var summary = new TextBlock
        {
            Foreground = _isDarkTheme ? T.Muted : (Brush)new BrushConverter().ConvertFromString("#57606A"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _titleLabel = title;
        _summaryLabel = summary;
        text.Children.Add(title);
        text.Children.Add(summary);
        Grid.SetColumn(text, 3);
        row.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (def.IsContainer)
            buttons.Children.Add(Ui.IconButton("plus",
                Loc.T("card.addchild.tip", "Add a step inside this block"),
                () => AddRequested?.Invoke()));
        var toggle = new ToggleGlyph(step.Enabled);
        toggle.Click += () =>
        {
            step.Enabled = toggle.On;
            RefreshSummary(summary);
            Toggled?.Invoke();
        };
        ToolTipService.SetToolTip(toggle, toggle.On
            ? Loc.T("card.toggle.on", "Enabled - click to disable")
            : Loc.T("card.toggle.off", "Disabled - click to enable"));
        buttons.Children.Add(toggle);
        buttons.Children.Add(Ui.IconButton("up",
            Loc.T("card.up.tip", "Move up"), () => MovedUp?.Invoke()));
        buttons.Children.Add(Ui.IconButton("down",
            Loc.T("card.down.tip", "Move down"), () => MovedDown?.Invoke()));
        buttons.Children.Add(Ui.IconButton("copy",
            Loc.T("card.copy.tip", "Duplicate"), () => Duplicated?.Invoke()));
        buttons.Children.Add(Ui.IconButton("trash",
            Loc.T("card.delete.tip", "Delete"), () => Deleted?.Invoke()));
        Grid.SetColumn(buttons, 4);
        row.Children.Add(buttons);

        Child = row;
        RefreshSummary(summary);
        ApplyState();

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.Source is ToggleGlyph or Button
                or FrameworkElement { Tag: "control" }) return;
            Selected?.Invoke(step.Id);
        };
        DragOver += (_, e) =>
        {
            e.Effects = _interactive
                && (e.Data.GetDataPresent("macro-action-type")
                    || e.Data.GetDataPresent("macro-step-id"))
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (!_interactive) return;
            if (e.Data.GetData("macro-action-type") is string typeId)
            {
                // Insert the new action right before this card.
                DropNew(typeId, step.Id);
            }
            else if (e.Data.GetData("macro-step-id") is string sourceId
                     && sourceId != step.Id)
            {
                DropMove(sourceId, step.Id);
            }
            e.Handled = true;
        };
    }

    // The card routes structural changes through callbacks set by the panel
    // (kept as public events so the card stays decoupled from the model).
    public event Action<string, string> DropNewRequested;   // typeId, beforeId
    public event Action<string, string> DropMoveRequested;  // srcId, beforeId

    private void DropNew(string typeId, string beforeId) =>
        DropNewRequested?.Invoke(typeId, beforeId);

    private void DropMove(string sourceId, string beforeId) =>
        DropMoveRequested?.Invoke(sourceId, beforeId);

    private void OnGripDown(object sender, MouseButtonEventArgs e)
    {
        Selected?.Invoke(_step.Id);
        if (!_interactive) return;
        var data = new DataObject("macro-step-id", _step.Id);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        e.Handled = true;
    }

    public void SetInteractive(bool enabled)
    {
        _interactive = enabled;
        Opacity = enabled ? 1.0 : 0.85;
    }

    /// <summary>Re-read texts from the model (after properties changed).</summary>
    public void RefreshContent()
    {
        _titleLabel.Text = _step.Name is { Length: > 0 } ? _step.Name
            : Loc.ActionName(_def.TypeId);
        RefreshSummary(_summaryLabel);
        ApplyState();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyState();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        if (running) _done = false;
        ApplyState();
    }

    public void SetDone(bool done)
    {
        _done = done;
        if (done) _running = false;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_isDarkTheme)
        {
            BorderBrush = _selected ? T.Accent
                : _running ? (Brush)new BrushConverter().ConvertFromString("#D69A3A")
                : _done ? (Brush)new BrushConverter().ConvertFromString("#33573F")
                : T.Border;
            Background = _selected
                ? (Brush)new BrushConverter().ConvertFromString("#232A3A")
                : _running
                ? (Brush)new BrushConverter().ConvertFromString("#2E2B24")
                : _done
                ? (Brush)new BrushConverter().ConvertFromString("#1D3325")
                : T.Field;
        }
        else
        {
            BorderBrush = _selected
                ? (Brush)new BrushConverter().ConvertFromString("#0969DA")
                : _running
                ? (Brush)new BrushConverter().ConvertFromString("#D4A72C")
                : _done
                ? (Brush)new BrushConverter().ConvertFromString("#2DA44E")
                : (Brush)new BrushConverter().ConvertFromString("#D0D7DE");
            Background = _selected
                ? (Brush)new BrushConverter().ConvertFromString("#EBF5FF")
                : _running
                ? (Brush)new BrushConverter().ConvertFromString("#FFF8C5")
                : _done
                ? (Brush)new BrushConverter().ConvertFromString("#DAFBE8")
                : Brushes.White;
        }

        if (!_step.Enabled)
        {
            Opacity = 0.55;
        }
        else
        {
            Opacity = _interactive ? 1.0 : 0.85;
        }
    }

    private void RefreshSummary(TextBlock target)
    {
        if (_def.TypeId == "goto_step")
        {
            var targetId = _step.GetStr("target_step_id");
            if (string.IsNullOrEmpty(targetId))
            {
                target.Text = Loc.T("sum.goto.empty", "Chưa chọn lệnh đích");
                return;
            }
            var targetInfo = _workflow?.GetStepDisplayInfo(targetId);
            target.Text = targetInfo != null
                ? Loc.F("sum.goto.target", "Chuyển đến {0}", targetInfo.DisplayText)
                : Loc.F("sum.goto.target", "Chuyển đến {0}", Loc.F("prop.step_selector.deleted", "[Lệnh đã bị xóa: {0}]", targetId));
            return;
        }

        var summary = _def.Summary?.Invoke(_step.Params) ?? "";
        if (_def.IsContainer)
        {
            var inside = _step.Children.Count + _step.ElseChildren.Count;
            var insideText = Loc.F("card.inside", "{0} step(s) inside", inside);
            if (_step.ElseChildren.Count > 0)
                insideText += $"  (THEN {_step.Children.Count} / ELSE {_step.ElseChildren.Count})";
            summary = $"{insideText}  |  {summary}";
        }
        target.Text = summary;
    }
}

/// <summary>Enable/disable glyph button used on step cards.</summary>
public class ToggleGlyph : Border
{
    private readonly TextBlock _glyph;
    public bool On;
    public event Action Click;

    public ToggleGlyph(bool on)
    {
        On = on;
        Width = 22;
        Height = 22;
        CornerRadius = new CornerRadius(3);
        Cursor = Cursors.Hand;
        _glyph = new TextBlock
        {
            Text = "\u23FB",                       // ⏻
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _glyph;
        Tag = "control";
        Render();
        MouseLeftButtonDown += (_, e) => e.Handled = true;   // keep card unselected
        MouseLeftButtonUp += (_, _) =>
        {
            On = !On;
            Render();
            Click?.Invoke();
        };
    }

    private void Render()
    {
        _glyph.Foreground = On
            ? (Brush)new BrushConverter().ConvertFromString("#3FB96F")
            : (Brush)new BrushConverter().ConvertFromString("#6B6F78");
        ToolTip = On
            ? Macro.Localization.Loc.T("card.toggle.on", "Enabled - click to disable")
            : Macro.Localization.Loc.T("card.toggle.off", "Disabled - click to enable");
    }
}
