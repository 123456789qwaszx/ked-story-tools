using Ked.Presentation.Core;
using St.Authoring.Catalog;
using St.Authoring.Editing;

namespace St.Authoring.Reading;

/// <summary>
/// 카드 한 장 — 한 줄 안에서 같은 출처(<c>// card:E04</c> · <c>F02#2</c> — docs/PLAN.md P5)를 든 커맨드의 묶음(K7-4 · D16).
/// </summary>
/// <param name="Tag">출처 그대로 — <c>E04</c> · <c>F02#2</c>. 한 줄에 같은 카드를 두 번 펼치면 둘째부터 <c>#2</c>다.</param>
/// <param name="CardId">카드 ID — <c>#n</c>을 뗀 것.</param>
/// <param name="Commands">묶인 커맨드 — 줄의 차례. 시간(<c>pause</c>)은 들지 않는다.</param>
/// <param name="Name">카탈로그의 카드 이름. 카탈로그가 없거나 거기에 없는 카드면 null.</param>
/// <param name="Whole">
/// 카드의 묶음 하나와 커맨드가 같으면 true, 그 묶음에서 빠진 것이 있으면 false("일부" — U6), 어느 묶음에도 맞지 않거나 카드를 모르면 null.
/// 커맨드의 값을 고친 것은 따지지 않는다 — 출처는 값이 아니라 온 곳이다.
/// </param>
/// <param name="Elsewhere">
/// 같은 노드의 앞 줄 · 다음 줄에 이 카드의 <b>다른</b> 묶음이 있으면 "앞 줄" · "다음 줄"(둘 다면 "앞 줄 · 다음 줄") — 줄을 넘는 짝(B01 · M 계열)일 수 있다(U8).
/// 짝인지 따로 쓴 것인지는 가르지 않는다(카드에 그 표지가 없다). 없으면 null.
/// </param>
public sealed record CardGroup(
    string Tag,
    string CardId,
    IReadOnlyList<CommandAddress> Commands,
    string? Name,
    bool? Whole,
    string? Elsewhere);

/// <summary>
/// 줄의 카드 층을 읽는다(K7-4) — 출처 주석이 있는 줄에서만 선다(U9). 묶음은 한 줄 안에서만이다(U8).
/// 카드의 글(의도 · 해석)은 읽지 않는다(D17) — 이름과 묶음의 커맨드 이름만 쓴다.
/// </summary>
public static class CardGroups
{
    /// <summary>
    /// 줄 하나의 카드 — 처음 나온 차례. <paramref name="file"/>은 그 줄이 든 파일의 읽기(앞 · 다음 줄을 본다),
    /// <paramref name="text"/>는 그 원문이다.
    /// </summary>
    public static IReadOnlyList<CardGroup> Read(string text, YarnFileRead file, LineRead line, CardCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(line);

        List<Bundle> bundles = Bundles(text, line, catalog);

        if (bundles.Count == 0)
        {
            return [];
        }

        // 같은 읽기의 줄이 아니어도(다시 읽었다) LineId로 찾는다.
        int index = file.Lines.IndexOf(line) is >= 0 and int found ? found : file.Lines.FindIndex(l => l.LineId is not null && l.LineId == line.LineId);
        LineRead? previous = index > 0 && file.Lines[index - 1].NodeName == line.NodeName ? file.Lines[index - 1] : null;
        LineRead? next = index >= 0 && index + 1 < file.Lines.Count && file.Lines[index + 1].NodeName == line.NodeName ? file.Lines[index + 1] : null;

        List<Bundle> before = previous is null ? [] : Bundles(text, previous, catalog);
        List<Bundle> after = next is null ? [] : Bundles(text, next, catalog);

        return bundles.Select(b =>
        {
            List<string> where = [];

            if (Other(b, before))
            {
                where.Add("앞 줄");
            }

            if (Other(b, after))
            {
                where.Add("다음 줄");
            }

            return new CardGroup(b.Tag, b.CardId, b.Commands, b.Card?.Name, b.Whole, where.Count > 0 ? string.Join(" · ", where) : null);
        }).ToList();
    }

    private sealed record Bundle(string Tag, string CardId, List<CommandAddress> Commands, List<string> Names, Card? Card, bool? Whole, int? Part);

    // 이웃 줄에 같은 카드의 다른 묶음이 있는가 — 묶음을 알 때만.
    private static bool Other(Bundle bundle, List<Bundle> neighbours)
        => bundle.Part is int part && neighbours.Any(n => n.CardId == bundle.CardId && n.Part is int other && other != part);

    private static List<Bundle> Bundles(string text, LineRead line, CardCatalog? catalog)
    {
        IReadOnlyDictionary<int, string> tags = LineEditor.CardTags(text, line);
        List<Bundle> bundles = [];

        if (tags.Count == 0)
        {
            return bundles;
        }

        foreach (LineReadStep step in line.Steps)
        {
            foreach (LineReadCommand command in step.Commands)
            {
                if (command.Spec is null || command.Spec.Kind == CommandKind.Time
                    || !tags.TryGetValue(command.Line, out string? tag)
                    || CommandAddress.Of(line, command) is not { } address)
                {
                    continue;
                }

                Bundle? bundle = bundles.FirstOrDefault(b => b.Tag == tag);

                if (bundle is null)
                {
                    string id = tag.Split('#')[0];
                    bundle = new Bundle(tag, id, [], [], catalog?.Cards.FirstOrDefault(c => c.Id == id), null, null);
                    bundles.Add(bundle);
                }

                bundle.Commands.Add(address);
                bundle.Names.Add(command.Name);
            }
        }

        // 카드의 묶음과 견준다 — 같으면 온전하고, 그 묶음에 다 들면 일부다.
        return bundles.Select(b =>
        {
            if (b.Card is not { } card)
            {
                return b;
            }

            List<List<string>> parts = card.Parts.Select(p => p.Commands.Select(c => c.Name).ToList()).ToList();
            int whole = parts.FindIndex(p => SameBag(b.Names, p));

            if (whole >= 0)
            {
                return b with { Whole = true, Part = whole };
            }

            int partial = parts.FindIndex(p => Within(b.Names, p));
            return partial >= 0 ? b with { Whole = false, Part = partial } : b;
        }).ToList();
    }

    private static bool SameBag(List<string> a, List<string> b) => a.Count == b.Count && Within(a, b);

    private static bool Within(List<string> a, List<string> b)
    {
        List<string> rest = [.. b];
        return a.All(rest.Remove);
    }
}
