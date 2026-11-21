using Ked.Presentation.Core;

namespace St.Authoring.Reading;

/// <summary>
/// 칸에 적을 값의 후보 — 그 대본에 이미 쓰인 값이다(docs/PLAN.md §4.4 · Q3). 슬롯 · 표정 · 모션 · 이모지처럼
/// 목록에서 고르는 칸에 보인다. 후보에 없는 값도 적을 수 있다 — 값의 뜻은 런타임이 안다.
/// </summary>
public sealed class ValueCandidates
{
    private readonly Dictionary<string, List<string>> _byKey;

    private ValueCandidates(Dictionary<string, List<string>> byKey) => _byKey = byKey;

    /// <summary>그 칸의 후보 — 많이 쓰인 차례. 후보를 모으지 않는 칸이면 빈 목록.</summary>
    public IReadOnlyList<string> For(CommandArgSpec spec)
        => KeyOf(spec) is { } key && _byKey.TryGetValue(key, out List<string>? values) ? values : [];

    /// <summary>줄들에 적힌 값을 칸의 목록(슬롯 · 표정 · 모션 …)마다 모은다.</summary>
    public static ValueCandidates Collect(IEnumerable<LineRead> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        Dictionary<string, Dictionary<string, int>> counts = new(StringComparer.Ordinal);

        foreach (LineReadCommand command in lines.SelectMany(l => l.Steps).SelectMany(s => s.Commands))
        {
            foreach (BoundArg arg in command.Bound)
            {
                if (arg.Token is null || KeyOf(arg.Spec) is not { } key)
                {
                    continue;
                }

                if (!counts.TryGetValue(key, out Dictionary<string, int>? values))
                {
                    counts.Add(key, values = new Dictionary<string, int>(StringComparer.Ordinal));
                }

                values[arg.Token] = values.GetValueOrDefault(arg.Token) + 1;
            }
        }

        return new ValueCandidates(counts.ToDictionary(
            c => c.Key,
            c => c.Value.OrderByDescending(v => v.Value).ThenBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key).ToList(),
            StringComparer.Ordinal));
    }

    // 슬롯은 한 목록, 이름 칸은 명세 표가 적은 목록(face · motion · emoji …)마다.
    private static string? KeyOf(CommandArgSpec spec) => spec.Kind switch
    {
        ArgKind.Slot => "slot",
        ArgKind.Name when !string.IsNullOrEmpty(spec.Catalog) => spec.Catalog,
        _ => null,
    };
}
