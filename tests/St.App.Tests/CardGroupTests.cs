using Ked.Presentation.Core;

namespace St.App.Tests;

/// <summary>카드 묶음(K7-4) — 출처 주석으로 커맨드를 카드 한 장씩 묶어 보고, 딱지로 그 카드의 커맨드를 모두 고른다.</summary>
public class CardGroupTests
{
    // 지어낸 대본과 카탈로그 — 대사 원문도 실제 카드도 이 저장소에 넣지 않는다.
    private const string Script =
        "title: C\n---\n" +
        "<<at start move_by s1 3.27u 0u 0fr>> // card:E04\n<<at start fade_in s1 0.6s>> // card:E04\n" +
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
            { "id": "G01", "name": "뛰기", "class": "몸짓",
              "command": "// 줄 머리\n<<at start motion s1 g01_hop>>\n<<at start face_swap s1 <표정> 0.2s>>\n화자: 대사 …\n// 말이 꺾이는 대목\n<<at a motion s1 g01_hop>>\n화자: 앞 마디,[beat=a/] 뒷 마디." }
          ]
        }
        """;

    private static MainWindow Open(bool withCatalog = true)
    {
        string dir = Directory.CreateTempSubdirectory("st-k7-4-").FullName;
        File.WriteAllText(Path.Combine(dir, "c.yarn"), Script);

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
}
