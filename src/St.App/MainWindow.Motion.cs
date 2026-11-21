using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.App.Views;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Motions;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 줄의 모션(docs/PLAN.md §4.8) — 모션은 프리셋일 뿐이다. 고른 모션 커맨드를 이 줄의 것으로 풀고, 구운 키를 솎는다.
/// 모션 파일을 쓰는 길은 <see cref="EditSession"/>이다 — 대본의 고침과 한 칸으로 되돌린다.
/// </summary>
public partial class MainWindow
{
    /// <summary>펴 보인 모션을 이 대본 파일에서 트는 커맨드의 수. 펴 보인 모션이 없으면 0.</summary>
    internal int ShownMotionUses
        => ShownMotion is { } motion && _lines.SelectedItem is LibraryLine line ? MotionLibrary.Uses(line.File.Read, motion.Clip.Name) : 0;

    // 고른 커맨드가 아는 모션을 틀 때 — 누가 쓰는지와, 풀기 · 솎기.
    private void BuildMotionSection(bool editable)
    {
        if (ShownMotion is not { } motion)
        {
            return;
        }

        int uses = ShownMotionUses;
        bool shared = motion.Source == MotionSource.Shared;
        int keys = motion.Curves.Sum(c => c.Keys.Length);

        _inspector.Children.Add(new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new TextBlock { Text = $"모션 {motion.Clip.Name}", FontWeight = FontWeight.SemiBold, FontSize = 13 },
        });

        _inspector.Children.Add(FormGrid.Hint(
            (uses > 1 ? $"이 대본에서 {uses}곳이 함께 쓴다." : "이 줄만 쓴다.")
            + (shared ? " 모두가 쓰는 파일(motions.json)의 것이다." : " 이 편의 모션 파일에 있다.")
            + $" 커브 {motion.Curves.Count}개 · 키 {keys}개."));

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 6 };

        Button unpack = new() { Content = "이 줄의 것으로 풀기", IsEnabled = editable && (uses > 1 || shared) };
        unpack.Click += (_, _) => UnpackPickedMotion();
        ToolTip.SetTip(unpack, "모션을 이 줄만의 것으로 복사하고 이 커맨드가 그것을 가리키게 한다 — 원래의 모션과 그것을 쓰는 다른 줄은 그대로다");

        Button simplify = new() { Content = "키 솎기", IsEnabled = editable && !shared };
        simplify.Click += (_, _) => SimplifyPickedMotion();
        ToolTip.SetTip(simplify, "프레임마다 찍힌 키를 눈에 띄지 않는 한도(위치 0.5px · 크기 0.002배 · 회전 0.1°) 안에서 줄인다 — 남는 키는 원래의 것 그대로다");

        actions.Children.Add(unpack);
        actions.Children.Add(simplify);
        _inspector.Children.Add(actions);

        if (uses > 1 && !shared)
        {
            _inspector.Children.Add(FormGrid.Hint($"풀지 않고 솎으면 함께 쓰는 {uses - 1}곳도 같이 바뀐다.", Palette.Faint));
        }
        else if (uses <= 1 && !shared)
        {
            _inspector.Children.Add(FormGrid.Hint("이미 이 줄의 것이다 — 풀 것이 없다.", Palette.Faint));
        }
    }

    /// <summary>고른 모션 커맨드의 모션을 이 줄의 것으로 푼다 — 그 편의 모션 파일에 복사하고 대본의 이름을 바꾼다.</summary>
    internal EditOutcome? UnpackPickedMotion()
        => ChangePickedMotion((session, path, target) => session.UnpackMotion(path, target), "이 줄의 것으로 풀어 모션 파일과 대본에 썼다");

    /// <summary>고른 모션 커맨드의 모션에서 구운 키를 솎는다.</summary>
    internal EditOutcome? SimplifyPickedMotion()
    {
        int before = ShownMotion?.Curves.Sum(c => c.Keys.Length) ?? 0;
        EditOutcome? outcome = ChangePickedMotion((session, path, target) => session.SimplifyMotion(path, target), "키를 솎아 모션 파일에 썼다");

        if (outcome is { Ok: true })
        {
            int after = ShownMotion?.Curves.Sum(c => c.Keys.Length) ?? 0;
            _status.Text = outcome.Changed ? $"키를 솎아 모션 파일에 썼다 — {before}개 → {after}개 · 되돌리기 Ctrl+Z" : "더 솎을 키가 없다 — 한도 안에서 이미 가장 적다";
        }

        return outcome;
    }

    private EditOutcome? ChangePickedMotion(Func<EditSession, string, CommandAddress, EditOutcome> change, string status)
    {
        if (_session is null || _lines.SelectedItem is not LibraryLine line || PickedCommand is not { } target || _picked.Count != 1)
        {
            return null;
        }

        EditOutcome outcome = change(_session, line.File.Script.Path, target);

        if (!outcome.Ok)
        {
            _editMessage = $"고치지 않았다 — {DirectorText.Clean(outcome.Error)}";
            RefreshInspector();
            return outcome;
        }

        _editMessage = null;

        if (outcome.Changed)
        {
            ForgetMotions();
            ReloadKeepingSelection();
            _status.Text = $"{status} — Unity에서 되감으면 보인다 · 되돌리기 Ctrl+Z";
        }

        return outcome;
    }

    // ── 그래프 에디터 (characterRig의 것 — src/Mt.Graph/vendoring.md) ────────

    private readonly GraphEditorView _graph = new() { Height = 300 };

    private MotionGraphEditor? _graphEditor;
    private string? _graphPath;

    /// <summary>그래프 에디터 — 이 편의 모션 파일에 든 모션을 고를 때 타임라인 아래에 선다.</summary>
    internal GraphEditorView MotionGraph => _graph;

    /// <summary>그래프 에디터에 펴 보인 모션의 이름. 그래프가 서지 않았으면 null.</summary>
    internal string? GraphMotion { get; private set; }

    // 그래프를 그 모션에 묶는다 — 같은 모션이면 다시 묶지 않는다(고른 키와 보던 자리를 지킨다).
    private void BindGraph(MotionEntry? motion, string scriptPath)
    {
        if (motion is null || _session is null)
        {
            if (GraphMotion is not null)
            {
                GraphMotion = null;
                _graph.Bind(null, null);
            }

            return;
        }

        if (_graphEditor is null || _graphPath != scriptPath)
        {
            if (_graphEditor is not null)
            {
                _graphEditor.GraphChanged -= OnGraphChanged;
            }

            _graphEditor = new MotionGraphEditor(_session, scriptPath);
            _graphEditor.GraphChanged += OnGraphChanged;
            _graphPath = scriptPath;
            GraphMotion = null;
        }

        if (GraphMotion != motion.Clip.Name)
        {
            GraphMotion = motion.Clip.Name;
            _graph.RangeStart = 0;
            _graph.RangeEnd = motion.Clip.Duration;
            _graph.Bind(_graphEditor, GraphMotion);
        }
    }

    // 그래프에서 키를 고쳤다(파일에 쓰였다) — 되돌리기 단추와 상태만 따라간다. 줄은 다시 짓지 않는다.
    private void OnGraphChanged(object? sender, EventArgs e)
    {
        ForgetMotions();
        RefreshUndoButtons();
        _status.Text = _graphEditor?.Error is { } error
            ? $"고치지 않았다 — {error}"
            : "키를 고쳐 모션 파일에 썼다 — Unity에서 되감으면 보인다 · 되돌리기 Ctrl+Z";
    }

    /// <summary>대본을 새로 열었다 — 그래프가 쥔 편집기는 앞 대본의 것이다.</summary>
    private void ResetGraph()
    {
        if (_graphEditor is not null)
        {
            _graphEditor.GraphChanged -= OnGraphChanged;
        }

        (_graphEditor, _graphPath, GraphMotion) = (null, null, null);
        _graph.Bind(null, null);
    }
}
