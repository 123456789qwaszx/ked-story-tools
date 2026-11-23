using Ked.Presentation.Core;
using St.Authoring.Catalog;
using St.Authoring.Editing;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.Authoring.Tests;

/// <summary>카드 한 장을 통째로 빼고 · 다른 카드로 바꾸는 셈, 카드의 위키 주소(K7-5).</summary>
public class CardSwapTests
{
    // 지어낸 대본 — 간격과 지연(pause)이 걸린 앵커에 카드 E04와 출처 없는 커맨드.
    private const string Script =
        "title: D\n---\n" +
        "<<gap start 0.5s>>\n<<at start pause 5fr>>\n" +
        "<<at start move_by s1 3.27u 0u 0fr>> // card:E04\n<<at start fade_in s1 0.6s>> // card:E04\n<<at start face s1 05>>\n" +
        "가: 하나 #line:d1\n===\n";

    private static (YarnFileRead File, LineRead Line, CardGroup Card) Read(string text, string lineId, string tag)
    {
        YarnFileRead file = YarnLineReader.ReadFile(text, "yarn", StageCommandSpecTable.Default);
        Assert.True(file.TryGet(lineId, out LineRead line));
        CardGroup card = CardGroups.Read(text, file, line, CardCatalog.Load(CatalogTests.CatalogFolder())).Single(c => c.Tag == tag);
        return (file, line, card);
    }

    [Fact]
    public void 카드를_빼면_그_카드의_커맨드만_한_덩이로_빠지고_간격과_지연은_남는다()
    {
        (_, LineRead line, CardGroup card) = Read(Script, "d1", "E04");

        EditOutcome outcome = LineEditor.ApplyAll(Script, CardSwap.Remove(card));
        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(
            "title: D\n---\n<<gap start 0.5s>>\n<<at start pause 5fr>>\n<<at start face s1 05>>\n가: 하나 #line:d1\n===\n",
            outcome.Text);

        // 간격과 지연에는 출처가 없다 — 건드리지 않고 남았다고 알린다(U7).
        IReadOnlyList<string> left = CardSwap.Leftovers(line, card);
        Assert.Equal(2, left.Count);
        Assert.StartsWith("start의 간격 ", left[0]);
        Assert.StartsWith("start의 지연 +", left[1]);
    }

    [Fact]
    public void 간격도_지연도_없으면_남은_것이_없다()
    {
        YarnFileRead file = YarnLineReader.ReadFile(CatalogTests.CardScript, "yarn", StageCommandSpecTable.Default);
        Assert.True(file.TryGet("c1", out LineRead c1));
        CardGroup e04 = CardGroups.Read(CatalogTests.CardScript, file, c1, null).Single(c => c.Tag == "E04");

        Assert.Empty(CardSwap.Leftovers(c1, e04));
    }

    [Fact]
    public void 새_카드의_슬롯과_앵커를_옛_카드의_것으로_처음_나온_차례끼리_잇는다()
    {
        YarnFileRead file = YarnLineReader.ReadFile(CatalogTests.CardScript, "yarn", StageCommandSpecTable.Default);
        Assert.True(file.TryGet("c1", out LineRead c1));
        IReadOnlyList<CardGroup> cards = CardGroups.Read(CatalogTests.CardScript, file, c1, null);

        Assert.Equal(new[] { "s2" }, CardSwap.Slots(c1, cards.Single(c => c.Tag == "G01")));
        Assert.Equal(new[] { "s3" }, CardSwap.Slots(c1, cards.Single(c => c.Tag == "E04#2")));

        Assert.Equal(new Dictionary<string, string> { ["s1"] = "s2" }, CardSwap.InOrder(["s2"], ["s1"]));
        Assert.Equal(new Dictionary<string, string> { ["s1"] = "s2", ["s2"] = "s3" }, CardSwap.InOrder(["s2", "s3"], ["s1", "s1", "s2"]));

        // 옛 카드에 짝이 없는 것은 그대로 둔다.
        Assert.Equal(new Dictionary<string, string> { ["s1"] = "s2" }, CardSwap.InOrder(["s2"], ["s1", "s4"]));

        // 앵커도 같은 셈이다 — E04#2는 a에 서 있다.
        Assert.Equal(new[] { "a" }, CardSwap.Anchors(cards.Single(c => c.Tag == "E04#2")));
        Assert.Equal(new[] { "start" }, CardSwap.Anchors(cards.Single(c => c.Tag == "G01")));
    }

    [Fact]
    public void 위키_주소는_틀의_id_자리에_카드_ID를_넣고_http만_받는다()
    {
        Assert.Null(CardWiki.Link(null, "E04"));
        Assert.Null(CardWiki.Link("  ", "E04"));

        Assert.Equal("https://wiki.example/cards#E04", CardWiki.Link("https://wiki.example/cards#{id}", "E04")!.AbsoluteUri);
        Assert.Equal("https://wiki.example/cards/E04", CardWiki.Link(" https://wiki.example/cards/{id} ", "E04")!.AbsoluteUri);
        Assert.Equal("https://wiki.example/cards#E04", CardWiki.Link("https://wiki.example/cards", "E04")!.AbsoluteUri);

        Assert.Null(CardWiki.Link("javascript:alert({id})", "E04"));
        Assert.Null(CardWiki.Link("file:///C:/cards/{id}", "E04"));
        Assert.Null(CardWiki.Link("cards/{id}", "E04"));
    }

    [Fact]
    public void 카드의_글은_읽지_않는다()
    {
        // 카탈로그의 JSON에 intent · when · screen이 있어도 카드는 ID · 이름 · 분류 · 종류 · 묶음만 든다(D17 · U10).
        Assert.Equal(
            new[] { "Id", "Name", "Class", "Kind", "Parts" },
            typeof(Card).GetConstructors().Single().GetParameters().Select(p => p.Name));
    }
}
