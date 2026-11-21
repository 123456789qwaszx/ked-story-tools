using System.Text.Json;
using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace St.Authoring.Catalog;

/// <summary>카드의 커맨드에 든 인자 하나 — 값이 적혀 있거나, 채울 자리(<c>&lt;표정&gt;</c>)다.</summary>
/// <param name="Text">적힌 값. 채울 자리면 그 자리의 이름(표정 · 인물 …).</param>
public sealed record CardToken(string Text, bool Placeholder);

/// <summary>카드가 펼치는 커맨드 하나 — 어느 앵커에 무엇을.</summary>
public sealed record CardCommand(string Anchor, string Name, IReadOnlyList<CardToken> Args);

/// <summary>카드가 두는 간격 하나.</summary>
public sealed record CardGap(string Anchor, string Duration);

/// <summary>
/// 카드의 한 묶음 — 대사 줄 하나에 붙는 커맨드들. 카드의 본보기(<c>command</c>)는 대사 줄마다 끊긴다:
/// 묶음이 여럿이면 쓰는 자리가 다른 갈래이거나(줄 머리 · 말이 꺾이는 대목) 이어지는 줄이다(여는 줄 · 닫는 줄).
/// </summary>
/// <param name="Title">묶음의 이름 — 본보기의 주석 첫 줄. 없으면 "묶음 N".</param>
/// <param name="Example">본보기의 대사 줄 — 어떤 표지가 있어야 하는지 보인다. 없으면 빈 글.</param>
/// <param name="Notes">본보기의 주석.</param>
public sealed record CardPart(
    string Title,
    IReadOnlyList<CardCommand> Commands,
    IReadOnlyList<CardGap> Gaps,
    string Example,
    IReadOnlyList<string> Notes);

/// <summary>연출 카드 한 장(작업공간 <c>catalog/cards.json</c>).</summary>
/// <param name="Kind">연출가의 종류 — <c>kinds.json</c>이 카드의 분류를 잇는다. 분류가 그 표에 없으면 null.</param>
public sealed record Card(
    string Id,
    string Name,
    string Class,
    CommandKind? Kind,
    string Intent,
    string When,
    string Screen,
    IReadOnlyList<CardPart> Parts);

/// <summary>
/// 연출 카드의 카탈로그(docs/PLAN.md §4.4) — 작업공간의 <c>cards.json</c>(카드)과 <c>kinds.json</c>(분류 → 종류)을 읽는다.
/// 카드는 사람과 AI가 읽는 본보기(yarn 조각)로 적혀 있다 — 그 조각에서 <c>&lt;&lt;at&gt;&gt;</c> · <c>&lt;&lt;gap&gt;&gt;</c>을 읽어
/// "커맨드 더하기"로 펼칠 수 있는 꼴로 바꾼다. 읽기만 한다.
/// </summary>
public sealed partial class CardCatalog
{
    public const string CardsFile = "cards.json";
    public const string KindsFile = "kinds.json";

    private const string KindsFormat = "ked-catalog-kinds/1";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private CardCatalog(string folder, IReadOnlyList<Card> cards, IReadOnlyList<string> problems)
    {
        Folder = folder;
        Cards = cards;
        Problems = problems;
    }

    public string Folder { get; }

    /// <summary>카드 — 파일에 적힌 차례.</summary>
    public IReadOnlyList<Card> Cards { get; }

    /// <summary>읽다가 알게 된 것 — 분류가 표에 없는 카드, 읽지 못한 커맨드 줄.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>그 종류의 카드 — 펼칠 커맨드가 있는 것만.</summary>
    public IReadOnlyList<Card> OfKind(CommandKind kind)
        => Cards.Where(c => c.Kind == kind && c.Parts.Count > 0).ToList();

    [GeneratedRegex(@"<(?<name>[^<>]+)>")]
    private static partial Regex PlaceholderPattern();

    /// <summary>카탈로그 폴더를 읽는다. 두 파일이 없거나 읽지 못하면 <see cref="InvalidDataException"/>.</summary>
    public static CardCatalog Load(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        CardsDto cards = Read<CardsDto>(Path.Combine(folder, CardsFile));
        KindsDto kinds = Read<KindsDto>(Path.Combine(folder, KindsFile));

        if (kinds.Format != KindsFormat)
        {
            throw new InvalidDataException($"{KindsFile}의 판이 '{kinds.Format}'다 — 이 툴은 '{KindsFormat}'를 읽는다");
        }

        List<string> problems = [];
        List<Card> result = [];

        foreach (CardDto dto in cards.Cards ?? [])
        {
            if (string.IsNullOrEmpty(dto.Id))
            {
                continue;
            }

            string @class = dto.Class ?? string.Empty;
            CommandKind? kind = null;

            // 카드 하나만 분류와 다른 종류에 두는 칸(cards)이 분류(classes)를 이긴다.
            string? key = kinds.Cards is not null && kinds.Cards.TryGetValue(dto.Id, out string? own) ? own
                : kinds.Classes is not null && kinds.Classes.TryGetValue(@class, out string? byClass) ? byClass
                : null;

            if (key is not null && CommandKinds.TryParse(key, out CommandKind parsed))
            {
                kind = parsed;
            }
            else
            {
                problems.Add($"카드 {dto.Id} — 분류 '{@class}'가 {KindsFile}에 없다. 어느 종류에도 오르지 않는다");
            }

            result.Add(new Card(
                dto.Id, dto.Name ?? dto.Id, @class, kind, dto.Intent ?? string.Empty, dto.When ?? string.Empty,
                dto.Screen ?? string.Empty, ParseParts(dto.Id, dto.Command ?? string.Empty, problems)));
        }

        return new CardCatalog(folder, result, problems);
    }

    /// <summary>카드의 본보기(yarn 조각)를 묶음으로 가른다 — 대사 줄이 묶음을 닫는다.</summary>
    internal static IReadOnlyList<CardPart> ParseParts(string cardId, string snippet, List<string> problems)
    {
        List<CardPart> parts = [];
        List<CardCommand> commands = [];
        List<CardGap> gaps = [];
        List<string> notes = [];

        void Close(string example)
        {
            if (commands.Count > 0 || gaps.Count > 0)
            {
                parts.Add(new CardPart(notes.Count > 0 ? notes[0] : $"묶음 {parts.Count + 1}", [.. commands], [.. gaps], example, [.. notes]));
            }

            commands.Clear();
            gaps.Clear();
            notes.Clear();
        }

        foreach (string raw in snippet.Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                notes.Add(line[2..].Trim());
                continue;
            }

            // (인물 등장)처럼 괄호로 적은 것은 지문이다 — 커맨드도 대사도 아니다.
            if (line.StartsWith('(') && line.EndsWith(')'))
            {
                notes.Add(line);
                continue;
            }

            if (!line.StartsWith("<<", StringComparison.Ordinal))
            {
                Close(line);
                continue;
            }

            // 채울 자리(<표정> · <회상 배경>)는 낱말 하나로 바꿔 두었다가 되돌린다 — 이름에 공백이 있을 수 있다.
            List<string> names = [];
            int end = line.LastIndexOf(">>", StringComparison.Ordinal);
            string inner = end > 2 ? line[2..end] : string.Empty;
            string marked = PlaceholderPattern().Replace(inner, match =>
            {
                names.Add(match.Groups["name"].Value);
                return $"￼{names.Count - 1}";
            });

            if (!YarnCommandTextExtractor.TryParseCommand($"<<{marked}>>", cardId, out StageCommand command))
            {
                problems.Add($"카드 {cardId} — 커맨드 줄을 읽지 못했다: {line}");
                continue;
            }

            CardToken Token(string token)
                => token.Length > 1 && token[0] == '￼' && int.TryParse(token.AsSpan(1), out int index) && index < names.Count
                    ? new CardToken(names[index], Placeholder: true)
                    : new CardToken(token, Placeholder: false);

            if (command.Name == LineBeatSchedule.AtCommandName && command.Args.Count >= 2)
            {
                commands.Add(new CardCommand(command.Args[0], command.Args[1], command.Args.Skip(2).Select(Token).ToList()));
            }
            else if (command.Name == LineBeatSchedule.GapCommandName && command.Args.Count == 2)
            {
                gaps.Add(new CardGap(command.Args[0], command.Args[1]));
            }
            else
            {
                problems.Add($"카드 {cardId} — <<at>> · <<gap>>이 아닌 줄이다: {line}");
            }
        }

        Close(string.Empty);
        return parts;
    }

    private static T Read<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"{path} — 비어 있다");
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{path} — 읽지 못했다: {exception.Message}");
        }
    }

    // 작업공간의 두 파일에서 이 툴이 읽는 칸만.
    private sealed record CardsDto(List<CardDto>? Cards);

    private sealed record CardDto(string? Id, string? Name, string? Class, string? Intent, string? When, string? Screen, string? Command);

    private sealed record KindsDto(string? Format, Dictionary<string, string>? Classes, Dictionary<string, string>? Cards);
}
