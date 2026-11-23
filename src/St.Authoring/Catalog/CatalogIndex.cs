using Ked.Presentation.Core;

namespace St.Authoring.Catalog;

/// <summary>
/// 카탈로그 칸의 한 항목 — 어휘의 커맨드 하나나 카드 한 장(docs/PLAN.md §2.4 D14).
/// 카드의 글(의도 · 언제 · 해석)은 들지 않는다 — 그것은 팀의 위키에 있다(D17). 카드는 ID · 이름과 펼치는 커맨드의 이름만 든다.
/// </summary>
/// <param name="Id">커맨드는 이름, 카드는 ID.</param>
/// <param name="Summary">커맨드의 한 줄 설명(명세 표의 것). 카드는 빈 글.</param>
/// <param name="Fields">커맨드는 칸의 이름(없어도 되는 칸은 <c>[ ]</c>로 싼다), 카드는 펼치는 커맨드의 이름.</param>
public sealed record CatalogEntry(CommandKind Kind, string Id, string Name, string Summary, IReadOnlyList<string> Fields, bool IsCard);

/// <summary>
/// 카탈로그 칸에 무엇을 보일지 — 화면 없이 셈한다(K7-1).
/// 낱말 없이는 고른 종류의 것 전부, 낱말이 있으면 연출가의 종류 전부에서 찾는다 — 어느 종류인지 몰라도 찾게.
/// </summary>
public static class CatalogIndex
{
    /// <summary>그 종류의 어휘 — 명세 표의 차례. 바로 실행하는 명령은 앵커에 넣지 못하므로 빠진다.</summary>
    public static IReadOnlyList<CatalogEntry> Commands(StageCommandSpecTable specs, CommandKind kind)
    {
        ArgumentNullException.ThrowIfNull(specs);
        return specs.OfKind(kind).Where(s => !s.Immediate).Select(Entry).ToList();
    }

    /// <summary>그 종류의 카드 — 카탈로그의 차례. 카탈로그를 열지 않았으면 빈 목록.</summary>
    public static IReadOnlyList<CatalogEntry> Cards(CardCatalog? catalog, CommandKind kind)
        => catalog?.OfKind(kind).Select(Entry).ToList() ?? [];

    /// <summary>
    /// 칸에 보일 것 — 커맨드가 먼저, 카드가 뒤. <paramref name="query"/>가 비었으면 <paramref name="kind"/>의 것 전부이고,
    /// 있으면 <paramref name="kinds"/>의 차례로 종류마다 낱말이 모두 든 것(커맨드는 이름 · 설명, 카드는 ID · 이름 — 대소문자를 가리지 않는다).
    /// </summary>
    public static IReadOnlyList<CatalogEntry> Find(
        StageCommandSpecTable specs, CardCatalog? catalog, CommandKind kind, IReadOnlyList<CommandKind> kinds, string? query)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        string[] words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return [.. Commands(specs, kind), .. Cards(catalog, kind)];
        }

        bool Matches(CatalogEntry entry)
            => words.All(w => entry.Id.Contains(w, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || entry.Summary.Contains(w, StringComparison.OrdinalIgnoreCase));

        List<CatalogEntry> commands = [];
        List<CatalogEntry> cards = [];

        foreach (CommandKind each in kinds)
        {
            commands.AddRange(Commands(specs, each).Where(Matches));
            cards.AddRange(Cards(catalog, each).Where(Matches));
        }

        return [.. commands, .. cards];
    }

    private static CatalogEntry Entry(StageCommandSpec spec)
        => new(spec.Kind, spec.Name, spec.Name, spec.Summary ?? string.Empty,
            spec.Args.Select(a => a.Optional ? $"[{a.Label}]" : a.Label).ToList(), IsCard: false);

    private static CatalogEntry Entry(Card card)
        => new(card.Kind!.Value, card.Id, card.Name, string.Empty,
            card.Parts.SelectMany(p => p.Commands).Select(c => c.Name).Distinct().ToList(), IsCard: true);
}
