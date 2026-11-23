using Avalonia.Input;
using Ked.Presentation.Core;
using St.Authoring.Settings;

namespace St.App.Tests;

/// <summary>카탈로그 칸(K7-1) — 고른 종류의 어휘 전부와 카드가 늘 보인다. 줄 · 앵커 · 커맨드를 고르지 않아도.</summary>
public class CatalogColumnTests
{
    // 지어낸 대본과 카탈로그 — 대사 원문도 실제 카드도 이 저장소에 넣지 않는다.
    private const string Script =
        "title: T\n---\n" +
        "<<at start face s2 05>>\n<<at start fade_in s1 0.6s>> // card:E04\n<<at a face_swap s2 03>>\n" +
        "가: 하나 [beat=a/]둘 #line:t1\n===\n";

    private const string Kinds = """
        { "format": "ked-catalog-kinds/1", "classes": { "등장 · 퇴장": "placement", "몸짓": "acting" }, "cards": {} }
        """;

    private const string Cards = """
        {
          "cards": [
            { "id": "E04", "name": "옆에서 들어옴", "class": "등장 · 퇴장", "intent": "밖에서 걸어 들어온다",
              "command": "<<at start move_by s1 3.27u 0u 0fr>>\n<<at start fade_in s1 0.6s>>\n화자: 대사 …" },
            { "id": "G01", "name": "뛰기", "class": "몸짓", "intent": "놀라거나 신날 때",
              "command": "<<at start motion s1 g01_hop>>\n<<at start face_swap s1 <표정> 0.2s>>\n화자: 대사 …" }
          ]
        }
        """;

    private static (MainWindow Window, string Catalog, string Settings) Open()
    {
        string dir = Directory.CreateTempSubdirectory("st-k7-").FullName;
        File.WriteAllText(Path.Combine(dir, "a.yarn"), Script);

        string catalog = Directory.CreateTempSubdirectory("st-k7-catalog-").FullName;
        File.WriteAllText(Path.Combine(catalog, "kinds.json"), Kinds);
        File.WriteAllText(Path.Combine(catalog, "cards.json"), Cards);

        string settings = Path.Combine(Directory.CreateTempSubdirectory("st-k7-set-").FullName, "settings.json");
        MainWindow window = new(settings);
        window.Show();
        window.OpenPath(dir);
        return (window, catalog, settings);
    }

    [Fact]
    public void 아무것도_고르지_않아도_고른_종류의_어휘와_카드가_보인다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string catalog, _) = Open();

            // 줄도 앵커도 커맨드도 고르지 않았다 — 칸은 이미 서 있다(D14).
            Assert.Null(window.PickedAnchor);
            Assert.True(window.CatalogOpen);
            Assert.Equal(CommandKind.Acting, window.CatalogKind);
            Assert.Contains(window.CatalogShown, e => e.Id == "motion");
            Assert.DoesNotContain(window.CatalogShown, e => e.IsCard);

            // 종류를 누르면 그 종류의 것 전부로 바뀐다.
            window.ChooseCatalogKind(CommandKind.Camera);
            Assert.Equal(
                StageCommandSpecTable.Default.OfKind(CommandKind.Camera).Where(s => !s.Immediate).Select(s => s.Name),
                window.CatalogShown.Select(e => e.Id));

            // 카드 폴더를 고르면 그 종류의 카드가 함께 선다 — ID · 이름만.
            Assert.True(window.SetCatalog(catalog));
            window.ChooseCatalogKind(CommandKind.Placement);
            Assert.Equal(new[] { "E04" }, window.CatalogShown.Where(e => e.IsCard).Select(e => e.Id));

            // 줄을 고르고 커맨드를 골라도 칸은 그대로다 — 고른 것의 칸과 함께 본다(U5).
            window.SelectLine("t1");
            window.SelectStep("start");
            Assert.Contains(window.CatalogShown, e => e.Id == "E04");
            RenderShots.Save(window, "k7-1-catalog");
            window.Close();
        });
    }

    [Fact]
    public void 찾으면_여섯_종류에서_찾는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string catalog, _) = Open();
            window.SetCatalog(catalog);
            window.SelectLine("t1");

            window.ChooseCatalogKind(CommandKind.Camera);
            window.FindInCatalog("fade");
            Assert.Contains(window.CatalogShown, e => e.Id == "fade_in" && e.Kind == CommandKind.Placement);
            Assert.Contains(window.CatalogShown, e => e.Id == "fade_out");

            window.FindInCatalog("뛰기");
            Assert.Equal(new[] { "G01" }, window.CatalogShown.Select(e => e.Id));
            RenderShots.Save(window, "k7-1-find");

            // 낱말을 지우면 고른 종류로 돌아간다.
            window.FindInCatalog("");
            Assert.All(window.CatalogShown, e => Assert.Equal(CommandKind.Camera, e.Kind));
            window.Close();
        });
    }

    [Fact]
    public void 칸은_접히고_F2로_펴지며_폭과_접힘을_기억한다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, string settings) = Open();
            window.SelectLine("t1");

            window.SetCatalogWidth(320);
            Assert.Equal(320, window.CatalogWidth);

            Assert.True(window.PressKey(Key.F2));
            Assert.False(window.CatalogOpen);
            RenderShots.Save(window, "k7-1-folded");

            // 접어도 편 때의 폭은 남는다.
            Assert.Equal(320, window.CatalogWidth);
            window.Close();

            WindowLayout layout = ToolSettings.Load(settings).Window!;
            Assert.True(layout.CatalogClosed);
            Assert.Equal(320, layout.Catalog);

            MainWindow again = new(settings);
            again.Show();
            Assert.False(again.CatalogOpen);

            Assert.True(again.PressKey(Key.F2, inText: true));
            Assert.True(again.CatalogOpen);
            Assert.Equal(320, again.CatalogWidth);

            // 너무 좁은 폭은 받지 않는다.
            again.SetCatalogWidth(10);
            Assert.Equal(320, again.CatalogWidth);
            again.Close();
        });
    }
}
