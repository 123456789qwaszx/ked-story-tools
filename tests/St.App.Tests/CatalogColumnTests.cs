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
        "가: 하나 [beat=a/]둘 #line:t1\n\n" +
        "<<fade_out s2 10fr>>\n나: 실행되지 않는 명령 #line:t2\n===\n";

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

    private static (MainWindow Window, string Catalog, string Settings) Open() => Open(out _);

    private static (MainWindow Window, string Catalog, string Settings) Open(out string script)
    {
        string dir = Directory.CreateTempSubdirectory("st-k7-").FullName;
        script = Path.Combine(dir, "a.yarn");
        File.WriteAllText(script, Script);

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

    // ── 여기서 넣기(K7-2) ───────────────────────────────────────────────────

    [Fact]
    public void 아무것도_고르지_않은_줄에서_누르면_start에_넣을_칸이_선다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open(out string script);
            window.SelectLine("t1");
            Assert.Null(window.PickedAnchor);

            window.ChooseCatalogKind(CommandKind.Camera);
            Assert.True(window.AddFromCatalog("shot_zoom"));

            // start를 고른 채로 더하기 칸이 그 커맨드로 섰다 — 넣는 길은 더하기(K4-3)의 것 그대로다.
            Assert.Equal("start", window.PickedAnchor);
            MainWindow.PendingCommand pending = Assert.Single(window.Pending);
            Assert.Equal("shot_zoom", pending.Spec.Name);
            Assert.Equal("start", pending.Anchor);

            window.FillPending(0, 0, "1.2");
            RenderShots.Save(window, "k7-2-start");
            Assert.True(window.InsertPending()!.Changed);
            Assert.Contains("<<at start shot_zoom 1.2>>", File.ReadAllText(script));
            window.Close();
        });
    }

    [Fact]
    public void 고른_커맨드의_앵커가_먼저고_다음이_고른_앵커다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();
            window.SelectLine("t1");

            // 커맨드를 골라 두었으면 그 커맨드의 앵커에.
            // 커맨드를 고르면 카탈로그는 그 종류(표정)로 옮겨 간다 — 다른 종류를 넣으려면 그 종류를 누른다.
            Assert.True(window.SelectCommand("t1/a/expression/0"));
            Assert.Equal(CommandKind.Expression, window.CatalogKind);
            window.ChooseCatalogKind(CommandKind.Acting);
            Assert.True(window.AddFromCatalog("motion"));
            Assert.Equal("a", window.PickedAnchor);
            Assert.Equal("a", Assert.Single(window.Pending).Anchor);
            RenderShots.Save(window, "k7-2-from-command");

            // 앵커를 골라 두었으면 그 앵커에 — 다른 종류도 찾아서 누르면 그 종류로 선다.
            window.SelectStep("end");
            window.FindInCatalog("shot_zoom");
            Assert.True(window.AddFromCatalog("shot_zoom"));
            Assert.Equal("end", Assert.Single(window.Pending).Anchor);
            window.Close();
        });
    }

    [Fact]
    public void 카드를_누르면_그_카드의_묶음이_선다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string catalog, _) = Open();
            window.SetCatalog(catalog);
            window.SelectLine("t1");
            window.SelectStep("a");

            window.ChooseCatalogKind(CommandKind.Placement);
            Assert.True(window.AddFromCatalog("E04"));

            // 카드가 적은 앵커(start)가 이 줄에 있으니 거기에 — 카드 더하기(K4-3)의 규칙 그대로다.
            Assert.Equal(new[] { "move_by", "fade_in" }, window.Pending.Select(p => p.Spec.Name));
            Assert.All(window.Pending, p => Assert.Equal("start", p.Anchor));
            RenderShots.Save(window, "k7-2-card");
            window.Close();
        });
    }

    [Fact]
    public void 줄이_없거나_읽기만이거나_보이지_않는_항목이면_세우지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();

            // 고른 줄이 없다 — 상태 줄에 까닭.
            window.ChooseCatalogKind(CommandKind.Camera);
            Assert.False(window.AddFromCatalog("shot_zoom"));
            Assert.Contains("줄을 먼저 고른다", window.StatusLine);

            // 지금 칸에 보이지 않는 항목은 누를 수 없다.
            window.SelectLine("t1");
            Assert.False(window.AddFromCatalog("motion"));
            Assert.Empty(window.Pending);

            // 읽기만인 줄 — 오른쪽에 까닭.
            window.SelectLine("t2");
            Assert.False(window.AddFromCatalog("shot_zoom"));
            Assert.Empty(window.Pending);
            Assert.Contains("읽기만", window.EditMessage);
            RenderShots.Save(window, "k7-2-readonly");
            window.Close();
        });
    }

    // ── 바꾸기(K7-3) ────────────────────────────────────────────────────────

    [Fact]
    public void 고른_커맨드를_바꾸면_제자리에서_칸을_옮겨_적고_되돌리기_한_칸이다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open(out string script);
            window.SelectLine("t1");

            // 커맨드를 고르면 카탈로그가 그 종류로 옮겨 가고 커맨드마다 "바꾸기"가 선다.
            window.ChooseCatalogKind(CommandKind.Camera);
            Assert.True(window.SelectCommand("t1/start/placement/0"));
            Assert.Equal(CommandKind.Placement, window.CatalogKind);
            RenderShots.Save(window, "k7-3-buttons");

            // 꼭 적는 칸(슬롯)이 옮겨 적혀 다 찼다 — 바로 바꾼다. 길이도 옮겨 적고, 카드의 출처는 걷는다.
            Assert.True(window.ReplaceFromCatalog("fade_out")!.Changed);
            string text = File.ReadAllText(script);
            Assert.Contains("<<at start face s2 05>>\n<<at start fade_out s1 0.6s>>\n<<at a face_swap s2 03>>", text);
            Assert.DoesNotContain("card:E04", text);

            // 바꾼 커맨드를 고른 채다.
            Assert.Equal(CommandKind.Placement, window.PickedCommand!.Value.Kind);
            Assert.Contains(window.ShownOutline!.Steps.SelectMany(s => s.Commands), c => c.Name == "fade_out");
            RenderShots.Save(window, "k7-3-replaced");

            // 되돌리기 한 칸으로 처음과 같다.
            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(script));
            window.Close();
        });
    }

    [Fact]
    public void 꼭_적는_칸이_비면_채울_칸이_서고_채워서_바꾼다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open(out string script);
            window.SelectLine("t1");
            window.SelectCommand("t1/a/expression/0");

            // face_swap → face: 슬롯은 옮겨 적었지만 표정 칸은 옮기지 않는다(이름 칸) — 채울 칸이 선다.
            Assert.Null(window.ReplaceFromCatalog("face"));
            MainWindow.PendingCommand pending = window.ReplacePending!;
            Assert.Equal("face", pending.Spec.Name);
            Assert.Equal("s2", pending.Values[0]);
            Assert.Equal("", pending.Values[1]);
            Assert.Equal(Script, File.ReadAllText(script));

            pending.Values[1] = "07";
            RenderShots.Save(window, "k7-3-fill");
            Assert.True(window.CommitReplace()!.Changed);
            Assert.Contains("<<at a face s2 07>>\n가:", File.ReadAllText(script));
            Assert.Null(window.ReplacePending);
            window.Close();
        });
    }

    [Fact]
    public void 여럿을_골랐거나_같은_커맨드거나_고른_것이_바뀌면_바꾸지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open(out string script);
            window.SelectLine("t1");

            window.SelectCommand("t1/start/placement/0");
            Assert.Null(window.ReplaceFromCatalog("fade_in"));

            window.SelectCommand("t1/start/expression/0", KeyModifiers.Control);
            Assert.Null(window.ReplaceFromCatalog("fade_out"));
            Assert.Equal(Script, File.ReadAllText(script));

            // 채우던 바꾸기는 다른 것을 고르면 버려진다.
            window.SelectCommand("t1/a/expression/0");
            window.ReplaceFromCatalog("face");
            Assert.NotNull(window.ReplacePending);
            window.SelectStep("a");
            Assert.Null(window.ReplacePending);
            window.Close();
        });
    }
}
