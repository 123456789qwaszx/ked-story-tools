using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 고치기 — 고른 커맨드의 칸 · 지연 · 빼기, 고른 스텝의 간격, 여럿을 한꺼번에 빼기 · 복사 · 붙이기, 되돌리기.
/// 화면은 조작(<see cref="LineEdit"/>)을 지어 <see cref="EditSession"/>에 넘길 뿐이다 — yarn을 직접 쓰지 않는다.
/// 연출가는 <c>pause</c> · <c>gap</c>이라는 낱말을 보지 않는다: 지연과 간격으로 보인다.
/// </summary>
public partial class MainWindow
{
    private readonly StackPanel _inspector = new() { Margin = new Thickness(14), Spacing = 8 };

    // 칸을 고치면 오른쪽을 통째로 다시 짓는다 — 앞서 지은 칸이 포커스를 잃으며 보내는 고침은 버린다.
    private int _inspectorBuild;

    private string? _editMessage;
    private IReadOnlyList<CommandClip> _clipboard = [];

    // 값의 후보는 파일이 바뀔 때만 다시 모은다 — 칸마다 대본을 훑지 않는다.
    private (string Path, int Revision, ValueCandidates Candidates)? _candidates;

    /// <summary>마지막 고침이 거절된 까닭. 받아들여졌으면 null.</summary>
    internal string? EditMessage => _editMessage;

    /// <summary>복사해 둔 커맨드의 수.</summary>
    internal int ClipboardCount => _clipboard.Count;

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

        if (_picked.Count > 1)
        {
            BuildManyInspector(outline.Editable);
        }
        else if (command is not null)
        {
            BuildCommandInspector(command, outline.Editable);
        }
        else if (step is not null)
        {
            BuildStepInspector(step, outline.Editable);
        }
        else
        {
            _inspector.Children.Add(FormGrid.Hint(
                "가운데에서 커맨드를 누르면 그 칸을, 앵커(스텝의 머리)를 누르면 그 간격을 여기서 고치고 거기에 더한다. " +
                "Ctrl · Shift로 여럿을 고른다. 단축키는 F1."));

            if (_clipboard.Count > 0 && outline.Editable)
            {
                _inspector.Children.Add(PasteButton());
            }
        }

        if (!outline.Editable && (command is not null || step is not null))
        {
            _inspector.Children.Add(FormGrid.Hint($"읽기만 — {DirectorText.Clean(outline.ReadOnlyReason)}", Palette.Warning));
        }

        if (_editMessage is { } message)
        {
            _inspector.Children.Add(FormGrid.Hint(message, Palette.Warning));
        }
    }

    private void BuildCommandInspector(OutlineCommand command, bool editable)
    {
        int build = _inspectorBuild;

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
        _inspector.Children.Add(FormGrid.Hint(command.Summary));

        FormGrid fields = new() { Margin = new Thickness(0, 6, 0, 0) };

        fields.Add("지연", TimeField(
            command.Delay > 0f ? TimeText.Token(command.Delay) : string.Empty, editable,
            text => Commit(build, new SetDelay(command.Address, text ?? "0s"))), "앵커가 열린 뒤 이 커맨드가 시작하기까지", DelaySpec);

        foreach (OutlineArg arg in command.Args)
        {
            Control editor = ArgField.Create(
                arg.Spec, arg.Written ? arg.Value : string.Empty,
                new ArgFieldOptions(editable, Candidates: Candidates(arg.Spec)),
                value => Commit(build, new SetValue(command.Address, arg.Name, value)));

            fields.Add(arg.Label, editor, arg.Spec.Note, arg.Spec);
        }

        _inspector.Children.Add(fields);

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };

        Button remove = new() { Content = "빼기", IsEnabled = editable };
        remove.Click += (_, _) => RemovePicked();
        ToolTip.SetTip(remove, "Delete");

        Button copy = new() { Content = "복사" };
        copy.Click += (_, _) => CopyPicked();
        ToolTip.SetTip(copy, "Ctrl+C — 다른 줄에서 Ctrl+V로 붙인다");

        actions.Children.Add(remove);
        actions.Children.Add(copy);
        _inspector.Children.Add(actions);
        BuildMotionSection(editable);

        _inspector.Children.Add(FormGrid.Hint("지연을 바꾸면 같은 앵커에서 이 뒤에 오는 커맨드도 같은 만큼 움직인다. 이 커맨드만 옮기려면 타임라인에서 막대를 끈다.", Palette.Faint));
    }

    private void BuildManyInspector(bool editable)
    {
        _inspector.Children.Add(new TextBlock { Text = $"커맨드 {_picked.Count}개를 골랐다", FontSize = 15, FontWeight = FontWeight.SemiBold });
        _inspector.Children.Add(FormGrid.Hint(string.Join("\n", _picked.Select(a => $"{a.Anchor} · {CommandKinds.Label(a.Kind)} {a.Index + 1}번째")), Palette.Faint));

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };

        Button remove = new() { Content = "모두 빼기", IsEnabled = editable };
        remove.Click += (_, _) => RemovePicked();
        ToolTip.SetTip(remove, "Delete — 되돌리기 한 칸이다");

        Button copy = new() { Content = "복사" };
        copy.Click += (_, _) => CopyPicked();
        ToolTip.SetTip(copy, "Ctrl+C — 다른 줄에서 Ctrl+V로 붙인다");

        actions.Children.Add(remove);
        actions.Children.Add(copy);
        _inspector.Children.Add(actions);
    }

    private void BuildStepInspector(OutlineStep step, bool editable)
    {
        int build = _inspectorBuild;

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
        FormGrid fields = new() { Margin = new Thickness(0, 6, 0, 0) };

        fields.Add("간격", TimeField(
            step.Gap is { } gap ? TimeText.Token(gap) : string.Empty, editable,
            text => Commit(build, new SetGap(lineId, step.Anchor, text))), spec: DelaySpec);

        _inspector.Children.Add(fields);
        _inspector.Children.Add(FormGrid.Hint(step.Kind == LineAnchorKind.End
            ? "글이 다 보인 뒤, 넘김을 받기까지의 최소 시간이다."
            : "이 앵커가 열린 뒤 다음 앵커가 열리기까지의 최소 시간이다. 글이 있는 구간은 글이 쳐지는 동안 더 늘어난다.", Palette.Faint));

        if (_clipboard.Count > 0 && editable)
        {
            _inspector.Children.Add(PasteButton());
        }

        BuildAddSection(step, editable);
    }

    private Button PasteButton()
    {
        Button paste = new() { Content = $"붙이기 — 복사한 {_clipboard.Count}개" };
        paste.Click += (_, _) => PastePicked();
        ToolTip.SetTip(paste, "Ctrl+V — 같은 이름의 앵커가 있으면 거기에, 없으면 고른 앵커(없으면 start)에 붙는다");
        return paste;
    }

    // ── 칸 ──────────────────────────────────────────────────────────────────

    // 지연 · 간격의 칸 — 커맨드의 인자가 아니지만 길이처럼 끌어서 고친다(1프레임씩).
    private static readonly CommandArgSpec DelaySpec = new("time", "시간", ArgKind.Duration, optional: true);

    private static Control TimeField(string text, bool editable, Action<string?> commit)
    {
        Control box = ArgField.Create(DelaySpec, text, new ArgFieldOptions(editable), commit);
        ToolTip.SetTip(box, "시간 — 15fr(30fps의 프레임) · 0.5s. 비우면 없다");
        return box;
    }

    /// <summary>그 칸에 적을 값의 후보 — 고른 줄이 든 파일에 이미 쓰인 값(슬롯 · 표정 · 모션 …).</summary>
    private IReadOnlyList<string> Candidates(CommandArgSpec spec)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line)
        {
            return [];
        }

        string path = line.File.Script.Path;

        if (_candidates is not { } cached || cached.Path != path || cached.Revision != _session.Revision)
        {
            _candidates = cached = (path, _session.Revision, ValueCandidates.Collect(line.File.Read.Lines));
        }

        return cached.Candidates.For(spec);
    }

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
    /// 고른 줄에 조작 여럿을 한 덩이로 한다(카드 한 장 · 여럿 빼기 · 붙이기) — 되돌리기 한 칸이다.
    /// <paramref name="keepStep"/>이면 고친 뒤에도 앵커를 고른 채로 둔다(이어서 더할 수 있게).
    /// <paramref name="pick"/>을 주면 고친 뒤에 그 커맨드를 고른다.
    /// </summary>
    internal EditOutcome? ApplyEdits(IReadOnlyList<LineEdit> edits, bool keepStep, CommandAddress? pick = null)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line)
        {
            return null;
        }

        EditOutcome outcome = _session.Apply(line.File.Script.Path, edits);

        if (!outcome.Ok)
        {
            _editMessage = $"고치지 않았다 — {DirectorText.Clean(outcome.Error)}";
            RefreshInspector();
            return outcome;
        }

        _editMessage = null;

        if (!outcome.Changed)
        {
            return outcome;
        }

        // 고친 커맨드를 그대로 쥔다 — 지연을 바꾸면 주소의 순번이 달라질 수 있어 결과가 알려 준 주소로 옮긴다.
        // 타임라인에서 끈 막대는 그 주소를 그대로 쥔다(조작이 여럿이면 마지막 것의 주소는 다른 커맨드다).
        _picked.Clear();

        if (!keepStep && (pick ?? outcome.Target) is { } target)
        {
            _picked.Add(target);
        }

        ReloadKeepingSelection();
        _status.Text = outcome.Warnings.Count > 0
            ? $"고쳐서 파일에 썼다 — 알림: {DirectorText.Clean(outcome.Warnings[0])}"
            : "고쳐서 파일에 썼다 — Unity에서 되감으면 보인다 · 되돌리기 Ctrl+Z";
        return outcome;
    }

    /// <summary>고른 커맨드를 모두 뺀다 — 한 덩이로(되돌리기 한 칸).</summary>
    internal EditOutcome? RemovePicked()
        => _picked.Count == 0 ? null : ApplyEdits(CommandClips.Remove(_picked), keepStep: true);

    /// <summary>고른 커맨드를 복사해 둔다. 복사한 수를 돌려준다.</summary>
    internal int CopyPicked()
    {
        if (_picked.Count == 0 || ShownOutline is null || _lines.SelectedItem is not LibraryLine line)
        {
            return 0;
        }

        _clipboard = CommandClips.Copy(line.File.Script.Text, line.Read, _picked.ToHashSet());
        _status.Text = $"커맨드 {_clipboard.Count}개를 복사했다 — 다른 줄에서 Ctrl+V";
        RefreshInspector();
        return _clipboard.Count;
    }

    /// <summary>
    /// 복사해 둔 커맨드를 고른 줄에 붙인다 — 같은 이름의 앵커가 있으면 거기에, 없으면 고른 앵커(없으면 start)에.
    /// 지연과 간격은 따라오지 않는다.
    /// </summary>
    internal EditOutcome? PastePicked()
    {
        if (_clipboard.Count == 0 || ShownOutline is not { Editable: true } outline)
        {
            return null;
        }

        IReadOnlyList<string> anchors = outline.Steps.Select(s => s.Anchor).ToList();
        string fallback = PickedAnchor ?? LineBeatSchedule.StartBeat;
        return ApplyEdits(CommandClips.Paste(outline.Line.LineId, anchors, _clipboard, fallback), keepStep: PickedAnchor is not null);
    }

    internal bool Undo() => Moved(_session?.Undo() == true, "되돌려서 파일에 썼다");

    internal bool Redo() => Moved(_session?.Redo() == true, "다시 해서 파일에 썼다");

    /// <summary>디스크의 대본을 다시 읽는다 — 바깥에서 바뀐 것이 있으면 화면이 따라온다.</summary>
    internal bool RefreshFromDisk()
    {
        ForgetMotions();
        return Moved(_session?.Refresh() == true, "바깥에서 바뀐 대본을 다시 읽었다 — 그 파일의 되돌리기는 비웠다");
    }

    private bool Moved(bool moved, string status)
    {
        if (moved)
        {
            // 되돌린 칸이 모션 파일의 것일 수 있다.
            ForgetMotions();
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

    // ── 테스트 손잡이 ───────────────────────────────────────────────────────

    /// <summary>글로 쓴 주소(<c>LineId/앵커/종류/순번</c>)의 커맨드를 가운데에서 고른다. 보이지 않으면 false.</summary>
    internal bool SelectCommand(string address, Avalonia.Input.KeyModifiers modifiers = Avalonia.Input.KeyModifiers.None)
    {
        if (!CommandAddress.TryParse(address, out CommandAddress parsed, out _)
            || ShownOutline?.Steps.Any(s => s.Commands.Any(c => c.Address == parsed)) != true)
        {
            return false;
        }

        PickCommand(parsed, modifiers);
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

    /// <summary>오른쪽 맨 위에 선 칸(고른 커맨드나 스텝의 것)의 이름 — 차례대로. 더하기의 칸은 들지 않는다.</summary>
    internal IReadOnlyList<string> InspectorFields => _inspector.Children.OfType<FormGrid>().FirstOrDefault()?.Labels ?? [];

    /// <summary>오른쪽의 그 이름의 칸에 사람이 하듯 적는다. 그런 칸이 없거나 잠겨 있으면 false.</summary>
    internal bool EditField(string label, string text)
        => _inspector.Children.OfType<FormGrid>().Select(g => g.Cell(label)).FirstOrDefault(c => c is not null) is { } cell
           && ArgField.TrySet(cell, text);

    /// <summary>오른쪽의 그 이름의 칸에 딸린 후보 딱지의 수.</summary>
    internal int FieldCandidates(string label)
        => _inspector.Children.OfType<FormGrid>().Select(g => g.Cell(label)).FirstOrDefault(c => c is not null) is { } cell ? ArgField.CandidateCount(cell) : 0;

    /// <summary>그 칸의 후보를 모두 펴거나 다시 접는다. 펼 것이 없으면 false.</summary>
    internal bool ToggleFieldCandidates(string label)
        => _inspector.Children.OfType<FormGrid>().Select(g => g.Cell(label)).FirstOrDefault(c => c is not null) is { } cell && ArgField.ToggleCandidates(cell);

    /// <summary>오른쪽의 그 이름의 칸을 걸음 수만큼 끈 것과 같다. 끌 수 없는 칸이면 false.</summary>
    internal bool ScrubField(string label, int steps, bool fine = false)
    {
        foreach (FormGrid grid in _inspector.Children.OfType<FormGrid>())
        {
            if (grid.Cell(label) is { } cell && grid.SpecOf(label) is { } spec)
            {
                return ArgField.TryScrub(spec, cell, steps, fine);
            }
        }

        return false;
    }
}
