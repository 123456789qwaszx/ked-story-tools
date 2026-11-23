using Ked.Presentation.Core;

namespace St.App.Tests;

/// <summary>카드 묶음(K7-4) — 출처 주석으로 커맨드를 카드 한 장씩 묶어 보고, 딱지로 그 카드의 커맨드를 모두 고른다.</summary>
public class CardGroupTests
{
    // 지어낸 대본과 카탈로그 — 대사 원문도 실제 카드도 이 저장소에 넣지 않는다.
    private const string Script =
        "title: C\n---\n" +
        "<<gap start 0.5s>>\n<<at start move_by s1 3.27u 0u 0fr>> // card:E04\n<<at start fade_in s1 0.6s>> // card:E04\n" +
        "<<at start motion s2 g01_hop>> // card:G01\n<<at start face_swap s2 03 0.2s>> // card:G01\n" +
        "<<at a fade_in s3 0.6s>> // card:E04#2\n<<at start face s1 05>>\n" +
        "가: 하나 [beat=a/]둘 #line:c1\n\n" +
        "<<at a motion s2 g01_hop>> // card:G01\n" +
        "나: 앞 마디,[beat=a/] 뒷 마디 #line:c2\n\n" +
        "다: 출처가 없는 줄 #line:c3\n===\n";

    private const string Kinds = """
        { "format": "ked-catalog-kinds/1", "classes": { "등장 · 퇴장": "placement", "몸짓": "acting" }, "cards": {} }
        """;

    private const string Cards = """
        {
          "cards": [
            { "id": "E04", "name": "옆에서 들어옴", "class": "등장 · 퇴장",
              "command": "<<at start move_by s1 3.27u 0u 0fr>>\n<<at start fade_in s1 0.6s>>\n화자: 대사 …" },
            { "id": "E05", "name": "제자리에서 나타남", "class": "등장 · 퇴장",
              "command": "<<at start fade_in s1 0.4s>>\n화자: 대사 …" },
            { "id": "G01", "name": "뛰기", "class": "몸짓",
              "command": "// 줄 머리\n<<at start motion s1 g01_hop>>\n<<at start face_swap s1 <표정> 0.2s>>\n화자: 대사 …\n// 말이 꺾이는 대목\n<<at a motion s1 g01_hop>>\n화자: 앞 마디,[beat=a/] 뒷 마디." }
          ]
        }
        """;

    private static MainWindow Open(bool withCatalog = true) => Open(out _, withCatalog);

    private static MainWindow Open(out string script, bool withCatalog = true)
    {
        string dir = Directory.CreateTempSubdirectory("st-k7-4-").FullName;
        script = Path.Combine(dir, "c.yarn");
        File.WriteAllText(script, Script);

        MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-k7-4-set-").FullName, "settings.json"));
        window.Show();
        window.OpenPath(dir);

        if (withCatalog)
        {
            string catalog = Directory.CreateTempSubdirectory("st-k7-4-catalog-").FullName;
            File.WriteAllText(Path.Combine(catalog, "kinds.json"), Kinds);
            File.WriteAllText(Path.Combine(catalog, "cards.json"), Cards);
            Assert.True(window.SetCatalog(catalog));
        }

        return window;
    }

    [Fact]
    public void 카드_딱지를_누르면_그_카드의_커맨드가_모두_골라진다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open();
            window.SelectLine("c1");

            Assert.Equal(new[] { "E04", "G01", "E04#2" }, window.ShownCards.Select(c => c.Tag));
            Assert.Equal(new bool?[] { true, true, false }, window.ShownCards.Select(c => c.Whole));
            Assert.Equal("다음 줄", window.ShownCards[1].Elsewhere);

            Assert.True(window.PickCard("E04"));
            Assert.Equal(
                new[] { "start/Placement/0", "start/Placement/1" },
                window.PickedCommands.Select(a => $"{a.Anchor}/{a.Kind}/{a.Index}"));
            RenderShots.Save(window, "k7-4-chips");

            // 카드가 다른 종류에 걸쳐도 모두 — G01은 연기와 표정이다.
            Assert.True(window.PickCard("G01"));
            Assert.Equal(new[] { CommandKind.Acting, CommandKind.Expression }, window.PickedCommands.Select(a => a.Kind));

            // 종류 필터에 가려진 커맨드는 빠진다.
            window.SetKinds(CommandKind.Acting);
            Assert.True(window.PickCard("G01"));
            Assert.Equal(new[] { CommandKind.Acting }, window.PickedCommands.Select(a => a.Kind));

            Assert.False(window.PickCard("Z99"));
            window.Close();
        });
    }

    [Fact]
    public void 카드로_보면_앵커_아래에_카드_한_줄이고_펼치면_커맨드가_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open();
            window.SelectLine("c1");

            window.SetCardView(true);
            Assert.True(window.CardView);
            RenderShots.Save(window, "k7-4-cards");

            Assert.True(window.ToggleCardExpanded("G01"));
            Assert.Contains("G01", window.ExpandedCards);
            RenderShots.Save(window, "k7-4-expanded");

            // 다른 줄로 옮기면 펼친 카드는 접힌다 — 카드로 보기는 그대로다.
            window.SelectLine("c2");
            Assert.True(window.CardView);
            Assert.Empty(window.ExpandedCards);
            Assert.Equal("앞 줄", Assert.Single(window.ShownCards).Elsewhere);
            window.Close();
        });
    }

    [Fact]
    public void 출처가_없는_줄에는_카드가_없고_카탈로그가_없으면_묶기만_한다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(withCatalog: false);

            window.SelectLine("c3");
            Assert.Empty(window.ShownCards);

            window.SelectLine("c1");
            Assert.Equal(3, window.ShownCards.Count);
            Assert.All(window.ShownCards, c => Assert.Null(c.Name));
            window.Close();
        });
    }

    // ── 카드 빼기 · 바꾸기 · 위키(K7-5) ─────────────────────────────────────

    [Fact]
    public void 카드를_고르면_카드의_칸이_서고_통째로_빼면_되돌리기_한_칸이다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out string script);
            window.SelectLine("c1");

            Assert.True(window.PickCard("E04"));
            Assert.Equal("E04", window.PickedCardGroup!.Tag);
            RenderShots.Save(window, "k7-5-card");

            Assert.True(window.RemoveCard("E04")!.Changed);
            string text = File.ReadAllText(script);
            Assert.DoesNotContain("// card:E04\n", text);
            Assert.Contains("<<gap start 0.5s>>\n<<at start motion s2 g01_hop>> // card:G01", text);

            // 간격에는 출처가 없다 — 건드리지 않고 남았다고 알린다(U7).
            Assert.Contains("카드 E04를 뺐다", window.StatusLine);
            Assert.Contains("start의 간격", window.StatusLine);

            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(script));
            window.Close();
        });
    }

    [Fact]
    public void 다른_카드로_바꾸면_옛_카드를_빼고_슬롯을_옮겨_새_카드를_넣는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out string script);
            window.SelectLine("c1");

            // E04#2는 a의 s3에 서 있다 — E05의 본보기는 start의 s1이다. 바꾼 카드는 옛 카드가 있던 자리에 선다.
            Assert.True(window.PickCard("E04#2"));
            Assert.Equal(CommandKind.Placement, window.CatalogKind);
            Assert.True(window.ReplaceCardFromCatalog("E05"));

            Assert.Equal("E04#2", window.ReplacingCard!.Tag);
            MainWindow.PendingCommand pending = Assert.Single(window.Pending);
            Assert.Equal("fade_in", pending.Spec.Name);
            Assert.Equal("s3", pending.Values[0]);
            Assert.Equal("a", pending.Anchor);
            RenderShots.Save(window, "k7-5-replace");

            Assert.True(window.InsertPending()!.Changed);
            string text = File.ReadAllText(script);
            Assert.DoesNotContain("card:E04#2", text);
            Assert.Contains("<<at a fade_in s3 0.4s>> // card:E05\n", text);
            Assert.Null(window.ReplacingCard);
            Assert.Contains("E04#2를 E05로 바꿨다", window.StatusLine);

            // 빼고 넣은 것이 되돌리기 한 칸이다.
            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(script));
            window.Close();
        });
    }

    [Fact]
    public void 바꾸기는_같은_카드나_카드를_고르지_않았으면_서지_않고_그만둘_수_있다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open();
            window.SelectLine("c1");

            Assert.False(window.ReplaceCardFromCatalog("E05"));

            window.PickCard("E04");
            Assert.False(window.ReplaceCardFromCatalog("E04"));

            Assert.True(window.ReplaceCardFromCatalog("E05"));
            window.CancelCardReplace();
            Assert.Null(window.ReplacingCard);

            // 다른 커맨드를 고르면 바꾸기는 그친다.
            window.PickCard("E04");
            Assert.True(window.ReplaceCardFromCatalog("E05"));
            window.SelectCommand("c1/start/expression/1");
            Assert.Null(window.ReplacingCard);
            window.Close();
        });
    }

    [Fact]
    public void 위키_주소_틀을_적으면_카드마다_링크가_서고_비우면_사라진다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open();
            window.SelectLine("c1");

            // 틀이 없으면 링크가 없다 — 위키는 아직 없을 수 있다.
            Assert.Null(window.CardWikiLink("E04"));

            window.SetCardWiki("https://wiki.example/cards#{id}");
            Assert.Equal("https://wiki.example/cards#E04", window.CardWikiLink("E04"));
            window.PickCard("E04");
            RenderShots.Save(window, "k7-5-wiki");

            window.SetCardWiki("");
            Assert.Null(window.CardWikiLink("E04"));
            window.Close();
        });
    }
}
