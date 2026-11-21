using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.Authoring.Editing;

/// <summary>
/// 타임라인에서 끌어 고친 것을 조작으로 바꾼다(docs/PLAN.md §4.6 · P3) — 프레임은 저장되지 않고
/// 지연(<c>pause</c>) · 길이 인자 · 간격(<c>&lt;&lt;gap&gt;&gt;</c>)으로 적힌다. 시각은 프레임(30fps)에 맞춘다.
/// </summary>
public static class TimelineEdits
{
    /// <summary>초를 가장 가까운 프레임의 때로.</summary>
    public static float Snap(float seconds)
        => MathF.Max(0f, MathF.Round(seconds * DurationToken.FramesPerSecond) / DurationToken.FramesPerSecond);

    /// <summary>
    /// 그 커맨드의 지연을 이보다 줄일 수 없다 — 바로 앞의 <c>pause</c>를 뺀, 더 앞선 커맨드의 기다림이다(지연 바꾸기가 건드리지 않는 몫).
    /// </summary>
    public static float DelayFloor(LineReadStep step, LineReadCommand command)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(command);

        int order = step.Commands.IndexOf(command);
        LineReadCommand? previous = order > 0 ? step.Commands[order - 1] : null;
        return command.Delay - (previous is { Name: YarnCommandTextExtractor.PauseCommand } ? previous.Hold : 0f);
    }

    /// <summary>
    /// 막대를 옆으로 — 그 커맨드의 지연을 <paramref name="delay"/>(초)로. 더 앞선 기다림보다 당길 수는 없다(거기서 멈춘다).
    /// <paramref name="alone"/>이면 그 막대만 옮긴다: 같은 앵커에서 바로 뒤에 오는 커맨드가 제자리에 남게 그 지연을 되맞춘다
    /// (늦추면 뒤 커맨드의 기다림이 줄고, 당기면 는다 — 뒤 커맨드가 이 막대보다 앞설 수는 없어, 넘어서면 함께 밀린다).
    /// 아니면 <c>pause</c>의 뜻 그대로 뒤의 커맨드가 모두 함께 움직인다. 바뀌는 것이 없으면 빈 목록.
    /// </summary>
    public static IReadOnlyList<LineEdit> MoveBar(LineRead line, CommandAddress address, float delay, bool alone)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!TryFind(line, address, out LineReadStep step, out LineReadCommand command))
        {
            return [];
        }

        float target = Snap(MathF.Max(delay, DelayFloor(step, command)));

        if (MathF.Abs(target - command.Delay) < 0.0005f)
        {
            return [];
        }

        List<LineEdit> edits = [new SetDelay(address, TimeText.Token(target))];

        if (alone)
        {
            // 바로 뒤의 커맨드(pause가 아닌 것) — 그 지연을 본디 값으로 되맞춘다. 이 막대보다 앞설 수는 없다.
            LineReadCommand? next = step.Commands
                .Skip(step.Commands.IndexOf(command) + 1)
                .FirstOrDefault(c => c.Name != YarnCommandTextExtractor.PauseCommand);

            if (next is not null && CommandAddress.Of(line, next) is { } nextAddress)
            {
                edits.Add(new SetDelay(nextAddress, TimeText.Token(MathF.Max(next.Delay, target))));
            }
        }

        return edits;
    }

    /// <summary>막대의 오른쪽 끝을 — 그 커맨드의 길이 칸을 <paramref name="seconds"/>로. 길이 칸이 없거나 바뀌는 것이 없으면 null.</summary>
    public static LineEdit? ResizeBar(LineRead line, CommandAddress address, float seconds)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!TryFind(line, address, out _, out LineReadCommand command) || command.Spec.DurationArgIndex < 0)
        {
            return null;
        }

        float target = Snap(seconds);

        if (command.Duration is { } now && MathF.Abs(target - now) < 0.0005f)
        {
            return null;
        }

        return new SetValue(address, command.Spec.Args[command.Spec.DurationArgIndex].Name, TimeText.Token(target));
    }

    /// <summary>
    /// 앵커의 세로줄을 — 그 앵커가 <paramref name="open"/>(초)에 열리게 앞 앵커의 간격을 맞춘다.
    /// 간격은 최소 시간이다: 글이 쳐지는 구간에서는 뒤로 미는 것만 되고, 당기면 간격이 걷힐 뿐 글의 속도보다 빨리 열리지 않는다.
    /// 첫 앵커이거나 바뀌는 것이 없으면 null.
    /// </summary>
    public static LineEdit? MoveAnchor(LineTimeline timeline, int anchor, float open)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        if (anchor <= 0 || anchor >= timeline.Anchors.Count || timeline.Line.LineId is null)
        {
            return null;
        }

        return GapOf(timeline, timeline.Anchors[anchor - 1], open);
    }

    /// <summary>줄의 끝(넘김을 받는 때)을 — 마지막 앵커(end)의 간격을 맞춘다.</summary>
    public static LineEdit? MoveReady(LineTimeline timeline, float ready)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return timeline.Anchors.Count == 0 || timeline.Line.LineId is null ? null : GapOf(timeline, timeline.Anchors[^1], ready);
    }

    // 그 앵커가 열린 때부터 until까지를 간격으로 — 기다림의 합보다 길지 않으면 적을 까닭이 없어 걷는다.
    private static LineEdit? GapOf(LineTimeline timeline, TimelineAnchor anchor, float until)
    {
        float gap = Snap(until - anchor.Open);
        float? wanted = gap > anchor.Settle + 0.0005f ? gap : null;

        if (wanted is null && anchor.Gap is null)
        {
            return null;
        }

        if (wanted is { } value && anchor.Gap is { } now && MathF.Abs(value - now) < 0.0005f)
        {
            return null;
        }

        return new SetGap(timeline.Line.LineId, anchor.Name, wanted is { } seconds ? TimeText.Token(seconds) : null);
    }

    private static bool TryFind(LineRead line, CommandAddress address, out LineReadStep step, out LineReadCommand command)
    {
        step = line.Step(address.Anchor);
        command = step?.Commands.FirstOrDefault(c => c.Spec?.Kind == address.Kind && c.KindIndex == address.Index)!;
        return command is not null;
    }
}
