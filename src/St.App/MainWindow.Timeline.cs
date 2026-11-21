using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.App;

/// <summary>
/// 타임라인(K4-4) — 고른 줄을 가로로 편다. 계산해서 보여 주는 면이다(docs/PLAN.md §4.6 · §6):
/// 기준 속도의 보통 재생에서만 참이고, 글이 있는 구간은 타자기 속도로 셈한 어림이다.
/// 끌어 고친 것은 지연 · 길이 · 간격의 조작이 되어 놓을 때 한 번 파일에 쓰인다.
/// </summary>
public partial class MainWindow
{
    private const double ZoomStep = 1.25;
    private const double MaxZoom = 12;

    private readonly TimelineView _timeline = new();
    private readonly ScrollViewer _timelineScroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    /// <summary>타임라인을 가로로 넓힌 배수 — 1이면 가운데의 폭에 꼭 맞는다.</summary>
    internal double TimelineZoom { get; private set; } = 1;
    private readonly MotionCurveView _curves = new();
    private readonly Dictionary<string, MotionLibrary> _motions = new(StringComparer.Ordinal);

    /// <summary>타임라인 아래에 커브를 펴 보인 모션 — 고른 커맨드가 아는 모션을 가리킬 때. 아니면 null.</summary>
    internal MotionEntry? ShownMotion { get; private set; }

    /// <summary>가운데에 펴 보인 줄의 타임라인. 고른 줄이 없으면 null.</summary>
    internal LineTimeline? ShownTimeline { get; private set; }

    /// <summary>타임라인이 쓰는 타자기의 속도(초당 글자 수) — 이 대본의 것.</summary>
    internal float TypewriterSpeed => _session is null ? St.Authoring.Settings.ToolSettings.DefaultTypewriterSpeed : _settings.TypewriterSpeedFor(_session.Root);

    private Control BuildTimeline(LibraryLine line)
    {
        // 처음 한 번만 잇는다 — 뷰는 줄을 옮겨도 그대로 쓴다.
        if (_timeline.Tag is null)
        {
            _timeline.Tag = this;
            _timeline.BarPicked += PickCommand;
            _timelineScroll.Content = new StackPanel { Spacing = 2, Children = { _timeline, _curves } };
            _timelineScroll.SizeChanged += (_, _) => FitTimeline();

            // Ctrl을 누르고 굴리면 넓히고 좁힌다 — 구르는 자리가 먼저 받기 전에 잡는다.
            _timelineScroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    SetTimelineZoom(TimelineZoom * Math.Pow(ZoomStep, e.Delta.Y));
                    e.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _timeline.Edited += (edits, bar) => ApplyEdits(edits, keepStep: false, pick: bar ?? PickedCommand);
        }

        string path = line.File.Script.Path;

        if (!_motions.TryGetValue(path, out MotionLibrary? clips))
        {
            _motions[path] = clips = MotionLibrary.Load(path);
        }

        // 트랙은 켜진 종류만 — 종류 필터가 트랙을 접고 편다.
        CommandKind[] tracks = DirectorKinds.Where(Kinds.Contains).ToArray();
        ShownTimeline = LineTimeline.Build(line.Read, Kinds, new TypewriterPacing { UnitsPerSecond = TypewriterSpeed }, clips);
        _timeline.Show(ShownTimeline, tracks, _picked.ToHashSet());

        // 고른 것이 모션 하나면 그 커브를 막대 아래에 편다 — 같은 시간의 눈금으로.
        TimelineBar? bar = _picked.Count == 1 ? ShownTimeline.Bars.FirstOrDefault(b => b.Address == _picked[0]) : null;
        OutlineCommand? picked = bar is null ? null : ShownOutline?.Steps.SelectMany(s => s.Commands).FirstOrDefault(c => c.Address == bar.Address);
        ShownMotion = picked is null ? null : clips.Find(picked);

        // 이 편의 파일에 든 모션이고 고칠 수 있는 줄이면 그래프 에디터로, 아니면 읽기만 하는 커브로.
        bool graph = ShownMotion is { Source: MotionSource.Episode } && ShownOutline?.Editable == true;
        _curves.Show(graph ? null : ShownMotion, bar?.Start ?? 0f, () => _timeline.Layout);
        BindGraph(graph ? ShownMotion : null, path);
        FitTimeline();

        TextBox speed = new()
        {
            Text = TypewriterSpeed.ToString("0.##", CultureInfo.InvariantCulture),
            Width = 52,
            FontSize = 11,
            Padding = new Thickness(4, 1),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        speed.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && float.TryParse(speed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                SetTypewriterSpeed(value);
                e.Handled = true;
            }
        };
        ToolTip.SetTip(speed, "타자기의 기준 속도(초당 글자 수) — 글이 쳐지는 구간의 어림에 쓴다. 적고 Enter. 이 대본에 기억해 둔다");

        StackPanel caption = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = $"타임라인 — 줄의 길이 {TimeText.Format(ShownTimeline.Ready)} · 기준 속도의 보통 재생 · 점선의 앵커와 그 뒤는 글의 속도로 셈한 어림 · 타자기 초당",
                    FontSize = 11,
                    Foreground = Palette.Faint,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                speed,
                new TextBlock { Text = "자", FontSize = 11, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center },
            },
        };

        TextBlock help = new()
        {
            Text = "막대를 옆으로 끌면 지연(그 막대만 — Shift를 누르면 뒤의 커맨드도 함께) · 오른쪽 끝을 끌면 길이 · 앵커의 세로줄을 끌면 앞 앵커의 간격 · 노란 줄을 끌면 넘김 전의 머묾 · Ctrl을 누르고 굴리면 넓힌다",
            FontSize = 11,
            Foreground = Palette.Faint,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        // 읽기만인 줄은 보이기만 한다 — 끌어도 거절될 뿐이라 아예 받지 않는다.
        _timeline.IsHitTestVisible = ShownOutline?.Editable == true;

        // 뷰는 하나라 앞서 선 자리에서 떼어 낸 뒤 새로 세운다.
        (_timelineScroll.Parent as Panel)?.Children.Remove(_timelineScroll);

        StackPanel panel = new() { Spacing = 4, Children = { caption, _timelineScroll } };

        if (ShownMotion is { } motion)
        {
            panel.Children.Add(MotionCaption(motion));
        }

        if (GraphMotion is not null)
        {
            (_graph.Parent as Panel)?.Children.Remove(_graph);
            panel.Children.Add(GraphBar());
            panel.Children.Add(_graph);
            panel.Children.Add(new TextBlock
            {
                Text = "그래프 — 키를 누르고(Shift로 여럿 · 끌어서 상자로) 가운데 단추로 끌어 옮긴다 · 두 번 누르면 그 자리에 키 · Delete로 지운다 · 핸들을 끌어 탄젠트 · "
                    + "Ctrl+1 auto · Ctrl+2 flat · 휠로 넓히고 Alt+가운데 단추로 민다 · F 고른 것에 맞춤 · A 전체. 구운 키는 여럿을 골라 늘이거나(R) 부드럽게(B) 끈다. 고칠 때마다 모션 파일에 쓴다",
                FontSize = 11,
                Foreground = Palette.Faint,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
        }

        panel.Children.Add(help);
        return panel;
    }

    // 펴 보인 모션이 무엇인가 — 어디서 왔고(프리셋 · 이 편의 것) 얼마나 길고 끝나면 어떻게 되는가.
    private static TextBlock MotionCaption(MotionEntry motion)
    {
        string source = motion.Source == MotionSource.Shared ? "프리셋" : "이 편의 것";
        string end = motion.Clip.End == MotionEnd.Restore ? "끝나면 제자리로 돌아온다" : "끝난 자리에 남는다";
        int keys = motion.Curves.Sum(c => c.Keys.Length);

        return new TextBlock
        {
            Text = $"모션 {motion.Clip.Name} — {source}({Path.GetFileName(motion.Path)}) · 길이 {TimeText.Format(motion.Clip.Duration)} · {end} · 커브 {motion.Curves.Count}개 · 키 {keys}개 · 점선은 움직이지 않을 때의 값",
            FontSize = 11,
            Foreground = Palette.Dim,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
    }

    /// <summary>모션 파일을 다시 읽게 한다 — 바깥에서 바뀌었을 수 있다.</summary>
    private void ForgetMotions() => _motions.Clear();

    /// <summary>타임라인을 가로로 넓힌다(1 = 가운데의 폭에 꼭 맞게). 넓히면 가로로 굴려 본다.</summary>
    internal void SetTimelineZoom(double zoom)
    {
        double clamped = Math.Clamp(zoom, 1, MaxZoom);

        if (Math.Abs(clamped - TimelineZoom) > 0.001)
        {
            TimelineZoom = clamped;
            FitTimeline();
        }
    }

    private void FitTimeline()
    {
        double viewport = _timelineScroll.Bounds.Width;

        // 자리를 잡기 전에는(폭 0) 뷰의 기본 폭에 맡긴다.
        _timeline.Width = viewport > 0 ? Math.Floor(viewport * TimelineZoom) : double.NaN;
        _curves.Width = _timeline.Width;
    }

    /// <summary>타자기의 속도를 바꾸고 이 대본에 기억해 둔다 — 글에 걸린 앵커의 어림이 따라 바뀐다.</summary>
    internal void SetTypewriterSpeed(float unitsPerSecond)
    {
        if (_session is null || unitsPerSecond <= 0f)
        {
            return;
        }

        _settings.TypewriterSpeeds[_session.Root] = unitsPerSecond;
        _settings.Save(_settingsPath);
        ShowSelectedLine();
    }

    // ── 테스트 손잡이 ───────────────────────────────────────────────────────

    /// <summary>타임라인에서 그 커맨드의 막대를 가로로 끈 것과 같다 — 초만큼. 놓을 때의 조작이 고쳐진다.</summary>
    internal EditOutcome? DragBar(string address, float seconds, bool together = false, bool end = false)
    {
        if (ShownTimeline is not { } timeline || _timeline.Layout is not { } layout
            || !CommandAddress.TryParse(address, out CommandAddress parsed, out _))
        {
            return null;
        }

        int index = timeline.Bars.ToList().FindIndex(b => b.Address == parsed);

        if (index < 0)
        {
            return null;
        }

        TimelineHit hit = new(end ? TimelineHitKind.BarEnd : TimelineHitKind.Bar, index);
        IReadOnlyList<LineEdit> edits = _timeline.Drag(hit, 0, seconds * layout.PixelsPerSecond, together);
        return edits.Count == 0 ? null : ApplyEdits(edits, keepStep: false, pick: parsed);
    }

    /// <summary>앵커의 세로줄을 끈 것과 같다. <paramref name="anchor"/>가 null이면 줄의 끝(넘김을 받는 때)이다.</summary>
    internal EditOutcome? DragAnchor(string? anchor, float seconds)
    {
        if (ShownTimeline is not { } timeline || _timeline.Layout is not { } layout)
        {
            return null;
        }

        TimelineHit hit = anchor is null
            ? new TimelineHit(TimelineHitKind.Ready, timeline.Anchors.Count - 1)
            : new TimelineHit(TimelineHitKind.Anchor, timeline.Anchors.ToList().FindIndex(a => a.Name == anchor));

        if (hit.Index < 0)
        {
            return null;
        }

        IReadOnlyList<LineEdit> edits = _timeline.Drag(hit, 0, seconds * layout.PixelsPerSecond, together: false);
        return edits.Count == 0 ? null : ApplyEdits(edits, keepStep: false, pick: PickedCommand);
    }
}
