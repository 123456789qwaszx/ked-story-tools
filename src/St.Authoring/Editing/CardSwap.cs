using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.Authoring.Editing;

/// <summary>
/// 카드 한 장을 통째로 빼고 · 다른 카드로 바꾸는 셈(K7-5 · D16) — 조작을 지을 뿐이고 쓰는 길은 <see cref="LineEditor"/> 그대로다.
/// 묶음은 한 줄 안의 것만(U8). 간격 · <c>pause</c>에는 출처가 없어(P5) 건드리지 않고, 남은 것을 알린다(U7).
/// </summary>
public static class CardSwap
{
    /// <summary>카드의 커맨드를 모두 빼는 조작 — 한 덩이로 한다(되돌리기 한 칸).</summary>
    public static IReadOnlyList<LineEdit> Remove(CardGroup card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return CommandClips.Remove(card.Commands);
    }

    /// <summary>
    /// 카드를 빼도 남는 것 — 카드의 커맨드가 선 앵커의 간격과, 카드의 커맨드 앞에 걸린 지연(<c>pause</c>).
    /// 카드가 둔 것인지는 모른다(출처가 없다) — 그 자리에 있다는 것만 알린다. 앵커의 차례로, 없으면 빈 목록.
    /// </summary>
    public static IReadOnlyList<string> Leftovers(LineRead line, CardGroup card)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(card);

        List<string> left = [];

        foreach (LineReadStep step in line.Steps)
        {
            List<LineReadCommand> mine = step.Commands
                .Where(c => CommandAddress.Of(line, c) is { } address && card.Commands.Contains(address))
                .ToList();

            if (mine.Count == 0)
            {
                continue;
            }

            if (step.HasGap)
            {
                left.Add($"{step.Name}의 간격 {TimeText.Format(step.Gap)}");
            }

            if (mine.Max(c => c.Delay) is > 0f and float delay)
            {
                left.Add($"{step.Name}의 지연 +{TimeText.Format(delay)}");
            }
        }

        return left;
    }

    /// <summary>카드의 커맨드가 쓴 슬롯 — 처음 나온 차례, 겹치지 않게.</summary>
    public static IReadOnlyList<string> Slots(LineRead line, CardGroup card)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(card);

        return line.Steps
            .SelectMany(s => s.Commands)
            .Where(c => CommandAddress.Of(line, c) is { } address && card.Commands.Contains(address))
            .SelectMany(c => c.Bound.Where(b => b.Spec.Kind == ArgKind.Slot && b.Token is not null).Select(b => b.Token!))
            .Distinct()
            .ToList();
    }

    /// <summary>카드의 커맨드가 선 앵커 — 처음 나온 차례, 겹치지 않게.</summary>
    public static IReadOnlyList<string> Anchors(CardGroup card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Commands.Select(a => a.Anchor).Distinct().ToList();
    }

    /// <summary>
    /// 새 카드의 슬롯 · 앵커를 옛 카드의 것으로 옮겨 적는 짝 — 처음 나온 차례끼리(새 카드의 첫째 → 옛 카드의 첫째 …).
    /// 옛 카드에 짝이 없는 것은 그대로 둔다. 카드의 본보기는 <c>s1</c> · <c>start</c>로 적혀 있고, 줄마다 누가 섰는지와
    /// 어느 대목에 거는지는 다르다 — 옛 카드가 그 줄에 맞춰 둔 것을 잇는다. 바꾼 카드는 옛 카드가 있던 자리에 선다.
    /// </summary>
    public static IReadOnlyDictionary<string, string> InOrder(IReadOnlyList<string> old, IReadOnlyList<string> fresh)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(fresh);

        Dictionary<string, string> map = [];
        List<string> distinct = fresh.Distinct().ToList();

        for (int i = 0; i < distinct.Count && i < old.Count; i++)
        {
            map[distinct[i]] = old[i];
        }

        return map;
    }
}
