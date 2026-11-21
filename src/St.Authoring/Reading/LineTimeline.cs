using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Authoring.Reading;

/// <summary>타임라인의 앵커 하나 — 세로줄.</summary>
/// <param name="Open">줄이 도착한 때부터 이 앵커가 열리기까지(초).</param>
/// <param name="Estimated">어림인가 — 글이 쳐지는 시간에 걸린 앵커(글 표지 · end)는 타자기 속도로 셈한 값이다.</param>
/// <param name="Gap">적어 둔 간격(초). 없으면 null.</param>
/// <param name="Settle">이 앵커의 커맨드가 모두 시작되기까지(초) — 간격을 이보다 줄여도 다음 앵커는 당겨지지 않는다.</param>
public sealed record TimelineAnchor(string Name, LineAnchorKind Kind, float Open, bool Estimated, float? Gap, float Settle, string Text);

/// <summary>타임라인의 막대 하나 — 커맨드 하나.</summary>
/// <param name="Start">줄이 도착한 때부터 시작하기까지(초) — 앵커가 열린 때 + 지연.</param>
/// <param name="Duration">길이(초). 모르면 null — 점으로 그린다.</param>
/// <param name="Delay">앵커가 열린 뒤의 지연(초).</param>
/// <param name="Floor">지연을 이보다 줄일 수 없다 — 더 앞선 커맨드의 기다림이다.</param>
/// <param name="Resizable">길이 칸이 있어 오른쪽 끝을 끌 수 있는가.</param>
public sealed record TimelineBar(
    CommandAddress Address,
    string Name,
    CommandKind Kind,
    string Anchor,
    float Start,
    float? Duration,
    float Delay,
    float Floor,
    bool Resizable,
    bool Estimated);

/// <summary>
/// 줄 하나를 가로로 편 것 — 툴의 타임라인이 그리는 면이다(docs/PLAN.md §4.6 · P3).
/// 계산해서 보여 줄 뿐 저장하지 않는다: 코어의 <see cref="LineTimetable"/>(기준 속도의 보통 재생)에 주소와 종류 필터를 얹었다.
/// 끌어 고친 것은 <see cref="TimelineEdits"/>가 조작으로 바꾼다.
/// </summary>
public sealed class LineTimeline
{
    private LineTimeline(LineRead line, IReadOnlyList<TimelineAnchor> anchors, IReadOnlyList<TimelineBar> bars, LineTimetable table)
    {
        Line = line;
        Anchors = anchors;
        Bars = bars;
        TypingStart = table.TypingStart;
        TypingEnd = table.TypingEnd;
        Ready = table.Ready;
    }

    public LineRead Line { get; }

    /// <summary>앵커 — 열리는 차례. 커맨드도 간격도 없는 표지는 들지 않는다(재생에서 서지 않는다).</summary>
    public IReadOnlyList<TimelineAnchor> Anchors { get; }

    /// <summary>막대 — 고른 종류의 커맨드. <c>pause</c>는 막대가 아니라 뒤 막대의 지연이다.</summary>
    public IReadOnlyList<TimelineBar> Bars { get; }

    /// <summary>타자기가 첫 글자를 치는 때(초).</summary>
    public float TypingStart { get; }

    /// <summary>타자기가 끝나는 때(초).</summary>
    public float TypingEnd { get; }

    /// <summary>넘김을 받을 수 있는 때(초) — 줄의 길이.</summary>
    public float Ready { get; }

    /// <summary>그리는 폭이 덮어야 하는 때(초) — 줄의 길이와 가장 늦게 끝나는 막대 가운데 뒤의 것.</summary>
    public float End => Math.Max(Ready, Bars.Count == 0 ? 0f : Bars.Max(b => b.Start + (b.Duration ?? 0f)));

    public static LineTimeline Build(LineRead line, IReadOnlySet<CommandKind> kinds, TypewriterPacing pacing, ClipLengths clips)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(pacing);
        ArgumentNullException.ThrowIfNull(clips);

        LineTimetable table = LineTimetable.Build(line, pacing, clips.LengthOf);

        List<TimelineAnchor> anchors = table.Steps
            .Select(s => new TimelineAnchor(
                s.Step.Name, s.Step.Kind, s.Open, s.Estimated, s.Step.HasGap ? s.Step.Gap : null, s.Step.Settle, s.Step.Text))
            .ToList();

        List<TimelineBar> bars = [];

        foreach (TimetableCommand entry in table.Commands)
        {
            LineReadCommand command = entry.Command;

            if (command.Spec is null || command.Spec.Kind == CommandKind.Time || !kinds.Contains(command.Spec.Kind)
                || CommandAddress.Of(line, command) is not { } address)
            {
                continue;
            }

            bars.Add(new TimelineBar(
                address, command.Name, command.Spec.Kind, entry.Step.Name, entry.Start, entry.Duration, command.Delay,
                TimelineEdits.DelayFloor(entry.Step, command), command.Spec.DurationArgIndex >= 0, entry.Estimated));
        }

        return new LineTimeline(line, anchors, bars, table);
    }
}
