using System.Text.Json;
using Ked.Presentation.Core;
using St.Authoring.Catalog;
using St.Authoring.Editing;
using St.Authoring.Reading;
using St.Authoring.Scripts;

namespace St.Cli;

/// <summary>
/// 카드 층(K7-6 — docs/PLAN.md §2.4 D16) — <c>st cards</c>(줄의 카드), <c>st remove … --card</c>(카드 한 장 빼기),
/// <c>st replace</c>(커맨드 바꾸기). 화면과 같은 도메인(<see cref="CardGroups"/> · <see cref="CardSwap"/> · <see cref="CommandSwap"/>)을 지난다.
/// 카드의 글(의도 · 해석)은 내지 않는다(D17) — 출처 · 이름 · 커맨드의 주소만.
/// </summary>
public static partial class StCli
{
    // ── 읽기 ────────────────────────────────────────────────────────────────

    private static int Cards(Arguments parsed, StageCommandSpecTable specs, TextWriter stdout)
    {
        parsed.Expect(1, "st cards <경로> [--line <LineId>] [--catalog <폴더>]");
        ScriptSet set = ScriptSet.Open(parsed.Positional[0]);
        CardCatalog? catalog = parsed.Catalog is null ? null : CardCatalog.Load(parsed.Catalog);

        if (parsed.Line is not null)
        {
            ScriptFile file = set.Find(parsed.Line, specs);
            YarnFileRead read = YarnLineReader.ReadFile(file.Text, file.Path, specs);
            read.TryGet(parsed.Line, out LineRead line);
            IReadOnlyList<CardGroup> cards = CardGroups.Read(file.Text, read, line, catalog);

            if (parsed.Json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { path = file.Path, lineId = Short(line), cards = cards.Select(CardJson).ToList() }, Output));
                return 0;
            }

            stdout.WriteLine($"# {file.Path}");
            stdout.WriteLine(LineView.Summary(line));
            stdout.WriteLine(cards.Count == 0 ? "  카드 없음 — 출처(// card:ID)가 든 커맨드가 없다" : $"  카드 {cards.Count}장");

            foreach (CardGroup card in cards)
            {
                stdout.WriteLine(CardLine(card));
            }

            return 0;
        }

        List<object> files = [];

        foreach (string path in set.Paths)
        {
            ScriptFile file = ScriptFile.Load(path);
            YarnFileRead read = YarnLineReader.ReadFile(file.Text, path, specs);
            List<(LineRead Line, IReadOnlyList<CardGroup> Cards)> lines = read.Lines
                .Select(l => (l, CardGroups.Read(file.Text, read, l, catalog)))
                .Where(x => x.Item2.Count > 0)
                .ToList();

            if (parsed.Json)
            {
                files.Add(new { path, lines = lines.Select(x => new { lineId = Short(x.Line), cards = x.Cards.Select(CardJson).ToList() }).ToList() });
                continue;
            }

            stdout.WriteLine($"# {path} — 카드가 있는 줄 {lines.Count}개(대사 줄 {read.Lines.Count}개 가운데)");

            foreach ((LineRead line, IReadOnlyList<CardGroup> cards) in lines)
            {
                stdout.WriteLine($"  {Short(line),-16} {line.Line,5}행  {string.Join(" ", cards.Select(c => c.Whole == false ? $"{c.Tag}(일부)" : c.Tag))}");
            }
        }

        if (parsed.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new { files }, Output));
        }

        return 0;
    }

    private static string Short(LineRead line) => line.LineId is null ? "(LineId 없음)" : CommandAddress.ShortLineId(line.LineId);

    // 주소의 뒤 세 칸 — st read --line과 같은 꼴(앞에 LineId를 붙이면 주소다).
    private static string Tail(CommandAddress address) => $"{address.Anchor}/{CommandKinds.Key(address.Kind)}/{address.Index}";

    private static string CardLine(CardGroup card)
    {
        string label = (card.Name ?? string.Empty) + (card.Whole == false ? " · 일부" : string.Empty);
        string elsewhere = card.Elsewhere is { } where ? $"  [{where}에 이 카드의 다른 묶음]" : string.Empty;
        return $"  {card.Tag,-8} {(label.Length > 0 ? label + "  " : string.Empty)}— 커맨드 {card.Commands.Count}: {string.Join(" ", card.Commands.Select(Tail))}{elsewhere}";
    }

    private static object CardJson(CardGroup card) => new
    {
        tag = card.Tag,
        cardId = card.CardId,
        name = card.Name,
        whole = card.Whole,
        elsewhere = card.Elsewhere,
        commands = card.Commands.Select(a => a.ToString()).ToList(),
    };

    // ── 고치기 ──────────────────────────────────────────────────────────────

    private static int RemoveCard(Arguments parsed, StageCommandSpecTable specs, TextWriter stdout, TextWriter stderr)
    {
        parsed.Expect(2, "st remove <경로> <LineId> --card <출처>");
        string lineId = parsed.Positional[1];
        string tag = parsed.Card!;

        return Write(parsed, specs, stdout, stderr, lineId, file =>
        {
            YarnFileRead read = YarnLineReader.ReadFile(file.Text, file.Path, specs);
            read.TryGet(lineId, out LineRead line);
            IReadOnlyList<CardGroup> cards = CardGroups.Read(file.Text, read, line, null);

            if (cards.FirstOrDefault(c => c.Tag == tag) is not { } card)
            {
                throw new InvalidDataException(cards.Count == 0
                    ? $"{lineId}에 카드가 없다 — 출처(// card:ID)가 든 커맨드가 없다"
                    : $"{lineId}에 카드 '{tag}'가 없다 — 있는 카드: {string.Join(" · ", cards.Select(c => c.Tag))}");
            }

            IReadOnlyList<string> left = CardSwap.Leftovers(line, card);
            IReadOnlyList<string> notes = left.Count > 0 ? [$"간격 · 지연은 건드리지 않았다(출처가 없다): {string.Join(" · ", left)}"] : [];
            return (CardSwap.Remove(card), notes);
        });
    }

    private static int Replace(Arguments parsed, StageCommandSpecTable specs, TextWriter stdout, TextWriter stderr)
    {
        parsed.ExpectAtLeast(3, "st replace <경로> <주소> <커맨드> [인자…]");
        CommandAddress target = parsed.Address(1);
        string name = parsed.Positional[2];
        string[] given = parsed.Positional.Skip(3).ToArray();

        return Write(parsed, specs, stdout, stderr, target.LineId, file =>
        {
            if (given.Length > 0 || !specs.TryGet(name, out StageCommandSpec spec))
            {
                return ([new ReplaceCommand(target, name, given)], []);
            }

            // 인자를 적지 않았다 — 슬롯 · 길이 · 이징을 옛 커맨드에서 옮겨 적는다(화면의 바꾸기와 같다).
            YarnFileRead read = YarnLineReader.ReadFile(file.Text, file.Path, specs);
            read.TryGet(target.LineId, out LineRead line);
            OutlineCommand? old = LineOutline.Build(file.Text, line, CommandKinds.All.ToHashSet())
                .Steps.SelectMany(s => s.Commands).FirstOrDefault(c => c.Address == target);

            if (old is null)
            {
                return ([new ReplaceCommand(target, name, given)], []);
            }

            string[] carried = CommandSwap.Carry(old.Args, spec);

            if (!LineEditor.TryComposeArguments(spec, carried, out IReadOnlyList<string> arguments, out string error))
            {
                throw new InvalidDataException(error);
            }

            IReadOnlyList<string> notes = arguments.Count > 0 ? [$"옛 커맨드에서 옮겨 적었다: {string.Join(" ", arguments)}"] : [];
            return ([new ReplaceCommand(target, name, arguments)], notes);
        });
    }
}
