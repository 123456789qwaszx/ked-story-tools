using Avalonia;
using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.App.Views;

/// <summary>타임라인에서 잡은 것.</summary>
internal enum TimelineHitKind
{
    None,

    /// <summary>막대의 몸 — 옆으로 끌면 지연.</summary>
    Bar,

    /// <summary>막대의 오른쪽 끝 — 끌면 길이.</summary>
    BarEnd,

    /// <summary>앵커의 세로줄 — 끌면 앞 앵커의 간격.</summary>
    Anchor,

    /// <summary>줄의 끝(넘김을 받는 때) — 끌면 end의 간격.</summary>
    Ready,
}

/// <param name="Index">막대나 앵커의 차례(<see cref="LineTimeline.Bars"/> · <see cref="LineTimeline.Anchors"/>). 잡은 것이 없으면 -1.</param>
internal readonly record struct TimelineHit(TimelineHitKind Kind, int Index)
{
    public static readonly TimelineHit None = new(TimelineHitKind.None, -1);
}

/// <summary>
/// 타임라인의 자리 셈 — 시각과 화면 좌표를 잇고, 막대의 자리를 정하고, 누른 곳에 무엇이 있는지 답한다.
/// 그리기(<see cref="TimelineView"/>)와 떼어 둔 순수 계산이다 — 화면 없이 시험한다.
///
///   위에서부터: 눈금 · 종류의 트랙(막대가 겹치면 그 트랙 안에서 줄을 나눈다) · 대사.
///   가로: 왼쪽에 트랙 이름의 칸, 그 오른쪽이 시간이다 — 줄의 길이가 폭에 맞게 들어온다.
/// </summary>
internal sealed class TimelineLayout
{
    public const double LabelWidth = 84;
    public const double RulerHeight = 32;
    public const double LaneHeight = 20;
    public const double TextHeight = 24;
    public const double RightMargin = 28;

    /// <summary>길이를 모르는 커맨드(표정 · 이모지 · 모션 파일에 없는 모션)를 그리는 점의 폭.</summary>
    public const double PointWidth = 10;

    // 끝을 잡는 폭 · 세로줄을 잡는 폭.
    private const double EdgeGrip = 6;
    private const double LineGrip = 5;

    // 줄이 아주 짧아도 1초는 편다 — 막대가 화면 폭만큼 부풀지 않게.
    private const float MinimumSpan = 1f;

    private readonly List<int> _lanes = [];
    private readonly Dictionary<CommandKind, (double Top, int Lanes)> _tracks = [];

    public TimelineLayout(LineTimeline timeline, IReadOnlyList<CommandKind> tracks, double width)
    {
        Timeline = timeline;
        Tracks = tracks;
        Width = Math.Max(width, LabelWidth + RightMargin + 100);
        PixelsPerSecond = (Width - LabelWidth - RightMargin) / Math.Max(timeline.End, MinimumSpan);

        // 트랙마다 겹치는 막대를 줄로 나눈다 — 시작이 이른 것부터, 들어갈 수 있는 첫 줄에.
        double top = RulerHeight;
        _lanes.AddRange(Enumerable.Repeat(0, timeline.Bars.Count));

        foreach (CommandKind kind in tracks)
        {
            List<double> ends = [];

            foreach (int index in Enumerable.Range(0, timeline.Bars.Count)
                         .Where(i => timeline.Bars[i].Kind == kind)
                         .OrderBy(i => timeline.Bars[i].Start).ThenBy(i => i))
            {
                (double left, double right) = Span(timeline.Bars[index]);
                int lane = ends.FindIndex(end => end <= left + 0.5);

                if (lane < 0)
                {
                    lane = ends.Count;
                    ends.Add(right);
                }
                else
                {
                    ends[lane] = right;
                }

                _lanes[index] = lane;
            }

            int lanes = Math.Max(1, ends.Count);
            _tracks[kind] = (top, lanes);
            top += lanes * LaneHeight + 4;
        }

        TextTop = top;
        Height = top + TextHeight;
    }

    public LineTimeline Timeline { get; }

    /// <summary>트랙의 종류 — 위에서부터.</summary>
    public IReadOnlyList<CommandKind> Tracks { get; }

    public double Width { get; }

    public double Height { get; }

    public double PixelsPerSecond { get; }

    /// <summary>대사 줄의 위쪽.</summary>
    public double TextTop { get; }

    /// <summary>시각(초) → 가로 좌표.</summary>
    public double X(float seconds) => LabelWidth + seconds * PixelsPerSecond;

    /// <summary>가로 좌표 → 시각(초). 시간 칸의 왼쪽 밖은 0이다.</summary>
    public float Seconds(double x) => (float)Math.Max(0, (x - LabelWidth) / PixelsPerSecond);

    /// <summary>그 종류의 트랙이 차지하는 세로 자리.</summary>
    public (double Top, double Height) Track(CommandKind kind)
        => _tracks.TryGetValue(kind, out (double Top, int Lanes) track) ? (track.Top, track.Lanes * LaneHeight) : (RulerHeight, 0);

    /// <summary>막대의 자리. 길이를 모르면 점의 폭이다. 트랙에 없는 종류면 빈 자리.</summary>
    public Rect BarRect(int index)
    {
        TimelineBar bar = Timeline.Bars[index];

        if (!_tracks.TryGetValue(bar.Kind, out (double Top, int Lanes) track))
        {
            return default;
        }

        (double left, double right) = Span(bar);
        return new Rect(left, track.Top + _lanes[index] * LaneHeight + 2, right - left, LaneHeight - 4);
    }

    /// <summary>누른 곳에 무엇이 있는가 — 눈금에서는 세로줄만, 그 아래에서는 막대가 세로줄보다 먼저다.</summary>
    public TimelineHit HitTest(Point point)
    {
        if (point.Y >= RulerHeight)
        {
            // 뒤에 그린 것(위에 놓인 것)부터 본다.
            for (int i = Timeline.Bars.Count - 1; i >= 0; i--)
            {
                Rect rect = BarRect(i);

                if (rect.Width <= 0 || !rect.Inflate(new Thickness(1, 0)).Contains(point))
                {
                    continue;
                }

                TimelineBar bar = Timeline.Bars[i];
                bool edge = bar.Resizable && bar.Duration is not null && point.X >= rect.Right - EdgeGrip;
                return new TimelineHit(edge ? TimelineHitKind.BarEnd : TimelineHitKind.Bar, i);
            }
        }

        if (Math.Abs(point.X - X(Timeline.Ready)) <= LineGrip)
        {
            return new TimelineHit(TimelineHitKind.Ready, Timeline.Anchors.Count - 1);
        }

        // 첫 앵커는 끌 것이 없다(앞 앵커가 없다). 겹치면 뒤의 것을 잡는다.
        for (int i = Timeline.Anchors.Count - 1; i >= 1; i--)
        {
            if (Math.Abs(point.X - X(Timeline.Anchors[i].Open)) <= LineGrip)
            {
                return new TimelineHit(TimelineHitKind.Anchor, i);
            }
        }

        return TimelineHit.None;
    }

    private (double Left, double Right) Span(TimelineBar bar)
    {
        double left = X(bar.Start);
        return (left, left + Math.Max(PointWidth, (bar.Duration ?? 0f) * PixelsPerSecond));
    }
}
