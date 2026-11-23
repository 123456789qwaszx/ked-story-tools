using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.App.Tests;

/// <summary>더하기(K4-3) — 고른 앵커에 어휘의 커맨드 하나를, 또는 카드 한 장을 펼쳐 넣는다.</summary>
public class AddingTests
{
    // 지어낸 대본과 카탈로그 — 대사 원문도 실제 카드도 이 저장소에 넣지 않는다.
    private const string Script =
        "title: T\n---\n" +
        "<<at start face s2 05>>\n<<at a face_swap s2 03>>\n" +
        "가: 하나 [beat=a/]둘 #line:t1\n\n" +
        "나: 커맨드가 없는 줄 #line:t2\n===\n";

    private const string Kinds = """
        { "format": "ked-catalog-kinds/1", "classes": { "등장 · 퇴장": "placement", "몸짓": "acting" }, "cards": {} }
        """;

    private const string Cards = """
        {
          "cards": [
            { "id": "E04", "name": "옆에서 들어옴", "class": "등장 · 퇴장", "intent": "밖에서 걸어 들어온다", "when": "새 사람이 올 때",
              "command": "// 줄 머리\n<<at start left s1 3.27u 0fr>>\n<<at start fade_in s1 0.6s>>\n<<gap start 0.5s>>\n화자: 대사 …" },
            { "id": "G01", "name": "뛰기", "class": "몸짓", "intent": "놀라거나 신날 때",
              "command": "// 줄 머리\n<<at start motion s1 g01_hop>>\n<<at start face_swap s1 <표정> 0.2s>>\n화자: 대사 …\n// 말이 꺾이는 대목\n<<at mid motion s1 g01_hop>>\n화자: 앞 마디,[beat=mid/] 뒷 마디." }
          ]
        }
        """;

    private static (MainWindow Window, string Path, string Catalog) Open(string lineId = "t1")
    {
        string dir = Directory.CreateTempSubdirectory("st-add-").FullName;
        string path = Path.Combine(dir, "a.yarn");
        File.WriteAllText(path, Script);

        string catalog = Directory.CreateTempSubdirectory("st-add-catalog-").FullName;
        File.WriteAllText(Path.Combine(catalog, "kinds.json"), Kinds);
        File.WriteAllText(Path.Combine(catalog, "cards.json"), Cards);

        MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-add-set-").FullName, "settings.json"));
        window.Show();
        window.OpenPath(dir);
        window.SelectLine(lineId);
        return (window, path, catalog);
    }

    [Fact]
    public void 어휘에서_커맨드를_골라_칸을_채워_넣는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, _) = Open();
            window.SelectStep("a");

            window.ChooseAddKind(CommandKind.Camera);
            Assert.True(window.ChooseAdd("shot_zoom"));
            Assert.Equal("a", window.Pending[0].Anchor);

            window.FillPending(0, 0, "1.2");
            RenderShots.Save(window, "k4-3-command");
            EditOutcome outcome = window.InsertPending()!;

            Assert.True(outcome.Changed);
            Assert.Contains("<<at a face_swap s2 03>>\n<<at a shot_zoom 1.2>>\n가:", File.ReadAllText(path));

            // 넣은 뒤에도 앵커를 고른 채다 — 채우던 것은 비워진다.
            Assert.Equal("a", window.PickedAnchor);
            Assert.Empty(window.Pending);
            window.Close();
        });
    }

    [Fact]
    public void 다른_종류의_커맨드는_고를_수_없고_꼭_적는_칸이_비면_넣지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, _) = Open();
            window.SelectStep("start");

            window.ChooseAddKind(CommandKind.Camera);
            Assert.False(window.ChooseAdd("face_swap"));
            Assert.False(window.ChooseAdd("surface_reset"));

            Assert.True(window.ChooseAdd("shot_zoom"));
            EditOutcome outcome = window.InsertPending()!;

            Assert.False(outcome.Ok);
            Assert.Contains("줌", window.EditMessage);
            Assert.Equal(Script, File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 커맨드가_없는_줄에도_더한다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, _) = Open("t2");
            window.SelectStep("end");

            window.ChooseAddKind(CommandKind.Emoji);
            window.ChooseAdd("emoji");
            window.FillPending(0, 0, "s1");
            window.FillPending(0, 1, "star");
            window.InsertPending();

            Assert.Contains("\n<<at end emoji s1 star>>\n나: 커맨드가 없는 줄 #line:t2\n", File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 카드_폴더를_고르면_그_종류의_카드가_서고_대본에_기억된다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, string catalog) = Open();
            window.SelectStep("start");

            // 카탈로그가 없으면 카드는 고를 수 없다.
            window.ChooseAddSource(cards: true);
            Assert.False(window.ChooseAdd("E04"));

            Assert.True(window.SetCatalog(catalog));
            window.ChooseAddSource(cards: true);
            window.ChooseAddKind(CommandKind.Placement);
            Assert.True(window.ChooseAdd("E04"));
            Assert.False(window.ChooseAdd("G01"));

            Assert.False(window.SetCatalog(Path.Combine(catalog, "없는폴더")));
            window.Close();
        });
    }

    [Fact]
    public void 카드를_펼치면_출처와_함께_한_덩이로_들어가고_되돌리기_한_칸이다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, string catalog) = Open();
            window.SetCatalog(catalog);
            window.SelectStep("start");
            window.ChooseAddSource(cards: true);
            window.ChooseAddKind(CommandKind.Placement);
            window.ChooseAdd("E04");

            Assert.Equal(new[] { "left", "fade_in" }, window.Pending.Select(p => p.Spec.Name));
            Assert.Equal(new[] { "s1", "3.27u", "0fr", "" }, window.Pending[0].Values);
            Assert.Equal(("start", "0.5s"), window.PendingGaps[0]);

            // 슬롯은 줄마다 다르다 — 넣기 전에 고친다.
            window.FillPending(0, 0, "s2");
            window.FillPending(1, 0, "s2");
            RenderShots.Save(window, "k4-3-card");
            Assert.True(window.InsertPending()!.Changed);

            Assert.Contains(
                "<<at start face s2 05>>\n<<at start left s2 3.27u 0fr>> // card:E04\n<<at start fade_in s2 0.6s>> // card:E04\n<<gap start 0.5s>>\n",
                File.ReadAllText(path));

            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(path));
            Assert.False(window.Undo());
            window.Close();
        });
    }

    [Fact]
    public void 카드의_채울_자리는_비어_있고_채우지_않으면_넣지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, string catalog) = Open();
            window.SetCatalog(catalog);
            window.SelectStep("start");
            window.ChooseAddSource(cards: true);
            window.ChooseAddKind(CommandKind.Acting);
            window.ChooseAdd("G01");

            Assert.Equal("표정", window.Pending[1].Placeholders[1]);
            Assert.Equal(string.Empty, window.Pending[1].Values[1]);

            Assert.Null(window.InsertPending());
            Assert.Contains("표정", window.EditMessage);
            Assert.Equal(Script, File.ReadAllText(path));

            window.FillPending(1, 1, "07");
            Assert.True(window.InsertPending()!.Changed);
            Assert.Contains("<<at start face_swap s1 07 0.2s>> // card:G01\n", File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 카드가_적은_앵커가_줄에_없으면_고른_앵커에_넣고_같은_카드는_둘째부터_번호가_붙는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, string catalog) = Open();
            window.SetCatalog(catalog);
            window.SelectStep("a");
            window.ChooseAddSource(cards: true);
            window.ChooseAddKind(CommandKind.Acting);
            window.ChooseAdd("G01");

            // 둘째 묶음은 표지 mid에 건다 — 이 줄에는 mid가 없어 고른 앵커(a)로 온다.
            window.ChooseCardPart(1);
            Assert.Equal(new[] { "a" }, window.Pending.Select(p => p.Anchor));
            window.InsertPending();
            Assert.Contains("<<at a motion s1 g01_hop>> // card:G01\n", File.ReadAllText(path));

            window.ChooseAdd("G01");
            window.ChooseCardPart(1);
            window.PlacePending(0, "end");
            window.InsertPending();
            Assert.Contains("<<at end motion s1 g01_hop>> // card:G01#2\n", File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 다음에_열면_기억해_둔_카드가_다시_선다()
    {
        HeadlessUi.Run(() =>
        {
            string dir = Directory.CreateTempSubdirectory("st-add-").FullName;
            File.WriteAllText(Path.Combine(dir, "a.yarn"), Script);
            string catalog = Directory.CreateTempSubdirectory("st-add-catalog-").FullName;
            File.WriteAllText(Path.Combine(catalog, "kinds.json"), Kinds);
            File.WriteAllText(Path.Combine(catalog, "cards.json"), Cards);
            string settings = Path.Combine(Directory.CreateTempSubdirectory("st-add-set-").FullName, "settings.json");

            MainWindow window = new(settings);
            window.Show();
            window.OpenPath(dir);
            window.SetCatalog(catalog);
            window.Close();

            MainWindow again = new(settings);
            again.Show();
            Assert.Equal(2, again.Catalog!.Cards.Count);
            again.Close();
        });
    }
}
