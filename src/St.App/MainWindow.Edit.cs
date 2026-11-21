using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 고치기(K4-2) — 고른 커맨드의 칸 · 지연 · 빼기, 고른 스텝의 간격, 되돌리기.
/// 화면은 조작(<see cref="LineEdit"/>)을 지어 <see cref="EditSession"/>에 넘길 뿐이다 — yarn을 직접 쓰지 않는다.
/// 연출가는 <c>pause</c> · <c>gap</c>이라는 낱말을 보지 않는다: 지연과 간격으로 보인다.
/// </summary>
public partial class MainWindow
{
    private readonly StackPanel _inspector = new() { Margin = new Thickness(14), Spacing = 8 };

    // 칸을 고치면 오른쪽을 통째로 다시 짓는다 — 앞서 지은 칸이 포커스를 잃으며 보내는 고침은 버린다.
    private int _inspectorBuild;

    private string? _editMessage;

    /// <summary>마지막 고침이 거절된 까닭. 받아들여졌으면 null.</summary>
    internal string? EditMessage => _editMessage;

    private Control BuildInspectorPanel()
        => new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _inspector };

    private void RefreshInspector()
    {
        _inspectorBuild++;
        _inspector.Children.Clear();

        LineOutline? outline = ShownOutline;

        if (outline is null)
        {
            return;
        }

        OutlineCommand? command = PickedCommand is { } address
            ? outline.Steps.SelectMany(s => s.Commands).FirstOrDefault(c => c.Address == address)
            : null;
        OutlineStep? step = PickedAnchor is { } anchor ? outline.Steps.FirstOrDefault(s => s.Anchor == anchor) : null;

        if (command is not null)
        {
            BuildCommandInspector(command, outline.Editable);
        }
        else if (step is not null)
        {
            BuildStepInspector(step, outline.Editable);
        }
        else
        {
            _inspector.Children.Add(Hint("가운데에서 커맨드를 누르면 그 칸을, 앵커(스텝의 머리)를 누르면 그 간격을 여기서 고친다."));
        }

        if (!outline.Editable && (command is not null || step is not null))
        {
            _inspector.Children.Add(Hint($"읽기만 — {outline.ReadOnlyReason}", Palette.Warning));
        }

        if (_editMessage is { } message)
        {
            _inspector.Children.Add(Hint(message, Palette.Warning));
        }
    }

    private void BuildCommandInspector(OutlineCommand command, bool editable)
    {
        _inspector.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Palette.Dot(command.Kind, 10),
                new TextBlock { Text = command.Name, FontFamily = Palette.Mono, FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = CommandKinds.Label(command.Kind), FontSize = 12, Foreground = Palette.Dim, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        _inspector.Children.Add(Hint(command.Summary));

        Grid fields = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 6, 0, 0) };

        AddField(fields, "지연", TimeBox(
            command.Delay > 0f ? TimeText.Token(command.Delay) : string.Empty, "0 — 앵커가 열리자마자", editable,
            text => new SetDelay(command.Address, text.Length == 0 ? "0s" : text)));

        foreach (OutlineArg arg in command.Args)
        {
            AddField(fields, arg.Label, ArgEditor(command, arg, editable), arg.Spec.Note);
        }

        _inspector.Children.Add(fields);

        Button remove = new() { Content = "이 커맨드 빼기", IsEnabled = editable, Margin = new Thickness(0, 8, 0, 0) };
        remove.Click += (_, _) => ApplyEdit(new RemoveCommand(command.Address));
        _inspector.Children.Add(remove);

        _inspector.Children.Add(Hint("지연을 바꾸면 같은 앵커에서 이 뒤에 오는 커맨드도 같은 만큼 움직인다.", Palette.Faint));
    }

    private void BuildStepInspector(OutlineStep step, bool editable)
    {
        _inspector.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Palette.Chip(step.Anchor, font: Palette.Mono),
                new TextBlock { Text = AnchorLabel(step.Kind), FontSize = 12, Foreground = Palette.Dim, VerticalAlignment = VerticalAlignment.Center },
            },
        });

        string lineId = ShownOutline!.Line.LineId;
        Grid fields = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 6, 0, 0) };

        AddField(fields, "간격", TimeBox(
            step.Gap is { } gap ? TimeText.Token(gap) : string.Empty, "없음", editable,
            text => new SetGap(lineId, step.Anchor, text.Length == 0 ? null : text)));

        _inspector.Children.Add(fields);
        _inspector.Children.Add(Hint(step.Kind == LineAnchorKind.End
            ? "글이 다 보인 뒤, 넘김을 받기까지의 최소 시간이다."
            : "이 앵커가 열린 뒤 다음 앵커가 열리기까지의 최소 시간이다. 글이 있는 구간은 글이 쳐지는 동안 더 늘어난다.", Palette.Faint));

        BuildAddSection(step, editable);
    }

    // ── 칸 ──────────────────────────────────────────────────────────────────

    private Control ArgEditor(OutlineCommand command, OutlineArg arg, bool editable)
    {
        int build = _inspectorBuild;
        CommandArgSpec spec = arg.Spec;

        // 낱말 칸 — 그 낱말을 적는가 마는가.
        if (spec.Flag && spec.Choices.Count > 0)
        {
            CheckBox box = new() { Content = spec.Choices[0], IsChecked = arg.Written, IsEnabled = editable };
            box.IsCheckedChanged += (_, _) => Commit(build, new SetValue(command.Address, arg.Name, box.IsChecked == true ? spec.Choices[0] : null));
            return box;
        }

        // 정해진 낱말 가운데 하나.
        if (spec.Choices.Count > 0)
        {
            List<string> items = [.. spec.Choices];
            const string unwritten = "(적지 않음)";

            if (spec.Optional)
            {
                items.Insert(0, unwritten);
            }

            ComboBox combo = new()
            {
                ItemsSource = items,
                SelectedIndex = arg.Written ? items.FindIndex(i => string.Equals(i, arg.Value, StringComparison.OrdinalIgnoreCase)) : spec.Optional ? 0 : -1,
                IsEnabled = editable,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string picked)
                {
                    Commit(build, new SetValue(command.Address, arg.Name, picked == unwritten ? null : picked));
                }
            };

            return combo;
        }

        // 그 밖 — 글로 적는다. 비우면 그 칸을 적지 않는다(생략값으로 간다).
        string written = arg.Written ? arg.Value : string.Empty;
        string placeholder = arg.Written || arg.Value.Length == 0 ? (spec.Optional ? "적지 않음" : "꼭 적는다") : $"{arg.Value} — 적지 않음";

        TextBox text = TextBoxFor(written, placeholder, editable, value => new SetValue(command.Address, arg.Name, value.Length == 0 ? null : value));
        IReadOnlyList<string> candidates = Candidates(spec);

        if (!editable || candidates.Count == 0)
        {
            return text;
        }

        // 후보를 누르면 바로 그 값으로 고친다.
        return new StackPanel
        {
            Spacing = 3,
            Children = { text, CandidateChips(candidates, written, value => Commit(build, new SetValue(command.Address, arg.Name, value))) },
        };
    }

    /// <summary>값의 후보를 누르는 딱지로 — 지금 값은 칠해 보인다. 많으면 앞의 것만 보인다.</summary>
    private static WrapPanel CandidateChips(IReadOnlyList<string> candidates, string current, Action<string> pick)
    {
        const int shown = 14;
        WrapPanel panel = new();

        foreach (string candidate in candidates.Take(shown))
        {
            Border chip = Palette.Chip(candidate, candidate == current ? null : Palette.Dim, candidate == current ? Palette.Picked : null, Palette.Mono);
            chip.Margin = new Thickness(0, 0, 4, 3);
            chip.Cursor = new Cursor(StandardCursorType.Hand);
            chip.PointerPressed += (_, _) => pick(candidate);
            panel.Children.Add(chip);
        }

        if (candidates.Count > shown)
        {
            panel.Children.Add(new TextBlock { Text = $"… {candidates.Count - shown}개 더", FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center });
        }

        return panel;
    }

    /// <summary>그 칸에 적을 값의 후보 — 고른 줄이 든 파일에 이미 쓰인 값(슬롯 · 표정 · 모션 …).</summary>
    private IReadOnlyList<string> Candidates(CommandArgSpec spec)
        => _lines.SelectedItem is LibraryLine line ? ValueCandidates.Collect(line.File.Read.Lines).For(spec) : [];

    private TextBox TimeBox(string text, string placeholder, bool editable, Func<string, LineEdit> edit)
    {
        TextBox box = TextBoxFor(text, placeholder, editable, edit);
        ToolTip.SetTip(box, "시간 — 15fr(30fps의 프레임) · 0.5s");
        return box;
    }

    /// <summary>글 칸 — Enter를 치거나 포커스가 떠날 때, 글이 바뀌었으면 고친다.</summary>
    private TextBox TextBoxFor(string text, string placeholder, bool editable, Func<string, LineEdit> edit)
    {
        int build = _inspectorBuild;
        TextBox box = new() { Text = text, PlaceholderText = placeholder, IsEnabled = editable, FontFamily = Palette.Mono, FontSize = 13 };

        void CommitText()
        {
            string now = (box.Text ?? string.Empty).Trim();

            if (now != text)
            {
                Commit(build, edit(now));
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = text;
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => CommitText();
        return box;
    }

    private static void AddField(Grid grid, string label, Control editor, string? note = null)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock caption = new() { Text = label, FontSize = 12, Foreground = Palette.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        editor.Margin = new Thickness(0, 0, 0, 4);

        if (!string.IsNullOrEmpty(note))
        {
            ToolTip.SetTip(caption, note);
        }

        Grid.SetRow(caption, row);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(caption);
        grid.Children.Add(editor);
    }

    private static TextBlock Hint(string text, IBrush? brush = null)
        => new() { Text = text, FontSize = 12, Foreground = brush ?? Palette.Dim, TextWrapping = TextWrapping.Wrap };

    // ── 조작 ────────────────────────────────────────────────────────────────

    private void Commit(int build, LineEdit edit)
    {
        if (build == _inspectorBuild)
        {
            ApplyEdit(edit);
        }
    }

    /// <summary>고른 줄에 조작 하나를 한다 — 받아들여지면 바로 파일에 쓰이고, 화면은 새로 읽은 대본으로 다시 선다.</summary>
    internal EditOutcome? ApplyEdit(LineEdit edit) => ApplyEdits([edit], keepStep: edit is SetGap);

    /// <summary>
    /// 고른 줄에 조작 여럿을 한 덩이로 한다(카드 한 장) — 되돌리기 한 칸이다.
    /// <paramref name="keepStep"/>이면 고친 뒤에도 앵커를 고른 채로 둔다(이어서 더할 수 있게).
    /// </summary>
    internal EditOutcome? ApplyEdits(IReadOnlyList<LineEdit> edits, bool keepStep)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line)
        {
            return null;
        }

        EditOutcome outcome = _session.Apply(line.File.Script.Path, edits);

        if (!outcome.Ok)
        {
            _editMessage = $"고치지 않았다 — {outcome.Error}";
            RefreshInspector();
            return outcome;
        }

        _editMessage = null;

        if (!outcome.Changed)
        {
            return outcome;
        }

        // 고친 커맨드를 그대로 쥔다 — 지연을 바꾸면 주소의 순번이 달라질 수 있어 결과가 알려 준 주소로 옮긴다.
        PickedCommand = keepStep ? null : outcome.Target;

        ReloadKeepingSelection();
        _status.Text = outcome.Warnings.Count > 0
            ? $"고쳐서 파일에 썼다 — 알림: {outcome.Warnings[0]}"
            : "고쳐서 파일에 썼다 — Unity에서 되감으면 보인다 · 되돌리기 Ctrl+Z";
        return outcome;
    }

    internal bool Undo() => Moved(_session?.Undo() == true, "되돌려서 파일에 썼다");

    internal bool Redo() => Moved(_session?.Redo() == true, "다시 해서 파일에 썼다");

    /// <summary>디스크의 대본을 다시 읽는다 — 바깥에서 바뀐 것이 있으면 화면이 따라온다.</summary>
    internal bool RefreshFromDisk() => Moved(_session?.Refresh() == true, "바깥에서 바뀐 대본을 다시 읽었다 — 그 파일의 되돌리기는 비웠다");

    private bool Moved(bool moved, string status)
    {
        if (moved)
        {
            _editMessage = null;
            ReloadKeepingSelection();
            _status.Text = status;
        }

        return moved;
    }

    private void RefreshUndoButtons()
    {
        _undoButton.IsEnabled = _session?.CanUndo == true;
        _redoButton.IsEnabled = _session?.CanRedo == true;
    }

    // 글 칸 안에서는 Ctrl+Z가 그 칸의 것이다(칸이 먼저 받는다) — 여기까지 올라온 것만 대본의 되돌리기다.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (control && e.Key == Key.Z && !shift)
        {
            e.Handled = Undo();
        }
        else if (control && (e.Key == Key.Y || (e.Key == Key.Z && shift)))
        {
            e.Handled = Redo();
        }
        else if (e.Key == Key.F5)
        {
            RefreshFromDisk();
            e.Handled = true;
        }
    }

    // ── 테스트 손잡이 ───────────────────────────────────────────────────────

    /// <summary>글로 쓴 주소(<c>LineId/앵커/종류/순번</c>)의 커맨드를 가운데에서 고른다. 보이지 않으면 false.</summary>
    internal bool SelectCommand(string address)
    {
        if (!CommandAddress.TryParse(address, out CommandAddress parsed, out _)
            || ShownOutline?.Steps.Any(s => s.Commands.Any(c => c.Address == parsed)) != true)
        {
            return false;
        }

        PickCommand(parsed);
        return true;
    }

    internal bool SelectStep(string anchor)
    {
        if (ShownOutline?.Steps.Any(s => s.Anchor == anchor) != true)
        {
            return false;
        }

        PickStep(anchor);
        return true;
    }

    /// <summary>
    /// 오른쪽의 그 이름의 칸에 사람이 하듯 적는다 — 글 칸은 적고 Enter, 고르는 칸은 그 낱말을 고르고, 낱말 칸은 "켬" · "끔".
    /// 그런 칸이 없으면 false.
    /// </summary>
    internal bool EditField(string label, string text)
    {
        foreach (Grid grid in _inspector.Children.OfType<Grid>())
        {
            TextBlock? caption = grid.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == label);

            if (caption is null)
            {
                continue;
            }

            Control? cell = grid.Children.FirstOrDefault(c => c != caption && Grid.GetRow(c) == Grid.GetRow(caption));

            // 후보가 딸린 칸은 글 칸과 후보를 한 덩이로 싼다.
            Control? editor = cell is StackPanel wrapped ? wrapped.Children.FirstOrDefault() : cell;

            switch (editor)
            {
                case TextBox box when box.IsEnabled:
                    box.Text = text;
                    box.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter });
                    return true;

                case ComboBox combo when combo.IsEnabled:
                    combo.SelectedItem = text;
                    return Equals(combo.SelectedItem, text);

                case CheckBox check when check.IsEnabled:
                    check.IsChecked = text == "켬";
                    return true;
            }
        }

        return false;
    }

    /// <summary>오른쪽 맨 위에 선 칸(고른 커맨드나 스텝의 것)의 이름 — 차례대로. 더하기의 칸은 들지 않는다.</summary>
    internal IReadOnlyList<string> InspectorFields
        => _inspector.Children.OfType<Grid>().Take(1).SelectMany(g => g.Children.OfType<TextBlock>()).Select(t => t.Text ?? string.Empty).ToList();
}
