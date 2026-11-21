using System.Globalization;
using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>
/// 수치를 끌어서 고친다(docs/PLAN.md §4.6) — 칸의 이름을 옆으로 끄는 만큼 값이 한 걸음씩 오르내린다.
/// 걸음은 칸의 갈래가 정한다: 길이는 1프레임(초로 적혀 있으면 0.05초), 거리 0.1, 각도 1, 배율 · 수 0.05.
/// 잘게(Shift)는 그 십분의 일이다(프레임은 그대로 1). 단위(u · fr · s)는 적힌 그대로 지킨다.
/// </summary>
public static partial class ValueScrub
{
    [GeneratedRegex(@"^(?<number>[+-]?(\d+\.?\d*|\.\d+))(?<unit>[A-Za-z%]*)$")]
    private static partial Regex NumberPattern();

    /// <summary>끌어서 고칠 수 있는 칸인가 — 수로 읽히는 갈래다.</summary>
    public static bool CanScrub(CommandArgSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Choices.Count == 0
            && spec.Kind is ArgKind.Duration or ArgKind.Distance or ArgKind.Angle or ArgKind.Scale or ArgKind.Number;
    }

    /// <summary>
    /// <paramref name="token"/>에서 <paramref name="steps"/>걸음 옮긴 값. 비어 있으면 생략값에서 시작한다.
    /// 수로 읽히지 않거나(이름 · 낱말) 끌 수 없는 칸이면 null. 길이는 0 아래로 내려가지 않는다.
    /// </summary>
    public static string? Step(CommandArgSpec spec, string? token, int steps, bool fine = false)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!CanScrub(spec))
        {
            return null;
        }

        string start = string.IsNullOrWhiteSpace(token) ? spec.Default ?? string.Empty : token.Trim();

        if (start.Length == 0)
        {
            start = spec.Kind == ArgKind.Duration ? "0fr" : spec.Kind == ArgKind.Distance ? "0u" : spec.Kind == ArgKind.Scale ? "1" : "0";
        }

        Match match = NumberPattern().Match(start);

        if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return null;
        }

        string unit = match.Groups["unit"].Value;
        bool frames = spec.Kind == ArgKind.Duration && unit.Equals("fr", StringComparison.OrdinalIgnoreCase);

        double pace = spec.Kind switch
        {
            ArgKind.Duration => frames ? 1 : 0.05,
            ArgKind.Distance => 0.1,
            ArgKind.Angle => 1,
            _ => 0.05,
        };

        if (fine && !frames)
        {
            pace /= 10;
        }

        double moved = Math.Round(value + (steps * pace), 4);

        if (spec.Kind == ArgKind.Duration)
        {
            moved = Math.Max(0, moved);
        }

        return moved.ToString("0.####", CultureInfo.InvariantCulture) + unit;
    }
}
