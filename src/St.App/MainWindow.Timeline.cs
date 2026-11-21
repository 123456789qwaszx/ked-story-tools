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
    private readonly TimelineView _timeline = new();
    private readonly Dictionary<string, ClipLengths> _clips = new(StringComparer.Ordinal);

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
            _timeline.Edited += (edits, bar) => ApplyEdits(edits, keepStep: false, pick: bar ?? PickedCommand);
        }

        string path = line.File.Script.Path;

        if (!_clips.TryGetValue(path, out ClipLengths? clips))
        {
            _clips[path] = clips = ClipLengths.Load(path);
        }

        // 트랙은 켜진 종류만 — 종류 필터가 트랙을 접고 편다.
        CommandKind[] tracks = DirectorKinds.Where(Kinds.Contains).ToArray();
        ShownTimeline = LineTimeline.Build(line.Read, Kinds, new TypewriterPacing { UnitsPerSecond = TypewriterSpeed }, clips);
        _timeline.Show(ShownTimeline, tracks, PickedCommand);

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
            Text = "막대를 옆으로 끌면 지연(그 막대만 — Shift를 누르면 뒤의 커맨드도 함께) · 오른쪽 끝을 끌면 길이 · 앵커의 세로줄을 끌면 앞 앵커의 간격 · 노란 줄을 끌면 넘김 전의 머묾",
            FontSize = 11,
            Foreground = Palette.Faint,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        // 읽기만인 줄은 보이기만 한다 — 끌어도 거절될 뿐이라 아예 받지 않는다.
        _timeline.IsHitTestVisible = ShownOutline?.Editable == true;

        // 뷰는 하나라 앞서 선 자리에서 떼어 낸 뒤 새로 세운다.
        (_timeline.Parent as Panel)?.Children.Remove(_timeline);

        return new StackPanel { Spacing = 4, Children = { caption, _timeline, help } };
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
