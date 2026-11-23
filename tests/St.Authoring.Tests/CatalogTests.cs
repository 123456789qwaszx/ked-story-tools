using Ked.Presentation.Core;
using St.Authoring.Catalog;
using St.Authoring.Editing;
using St.Authoring.Reading;
using St.Authoring.Settings;
using St.Cli;

namespace St.Authoring.Tests;

public class CatalogTests
{
    /// <summary>작업공간 <c>catalog/</c>의 꼴을 본뜬 지어낸 카탈로그 — 카드 넷과 분류 → 종류의 표.</summary>
    internal static string CatalogFolder()
    {
        string dir = Directory.CreateTempSubdirectory("st-catalog-").FullName;

        File.WriteAllText(Path.Combine(dir, CardCatalog.KindsFile), """
            {
              "format": "ked-catalog-kinds/1",
              "kinds": [ { "key": "camera", "label": "카메라" } ],
              "classes": { "카메라": "camera", "등장 · 퇴장": "placement", "몸짓": "acting", "화면": "screen" },
              "also": {},
              "cards": { "X09": "screen" }
            }
            """);

        File.WriteAllText(Path.Combine(dir, CardCatalog.CardsFile), """
            {
              "format": "시험",
              "cards": [
                { "id": "E04", "name": "옆에서 들어옴", "class": "등장 · 퇴장", "intent": "밖에서 걸어 들어온다", "when": "새 사람이 올 때", "screen": "옆에서 미끄러져 든다",
                  "command": "// 줄 머리\n<<at start move_by s1 3.27u 0u 0fr>>\n<<at start fade_in s1 0.6s>>\n화자: 대사 …" },
                { "id": "G01", "name": "뛰기", "class": "몸짓", "intent": "놀라거나 신날 때",
                  "command": "// 줄 머리\n<<at start motion s1 g01_hop>>\n<<at start face_swap s1 <표정> 0.2s>>\n화자: 대사 …\n// 말이 꺾이는 대목\n<<at a motion s1 g01_hop>>\n화자: 앞 마디,[beat=a/] 뒷 마디." },
                { "id": "B02", "name": "암전", "class": "화면",
                  "command": "<<at in screen_cover black 0.5s>>\n<<gap in 1s>>\n<<at start bg_sprite bg_main <회상 배경>>>\n(인물 등장)\n화자: [beat=in/]대사 …" },
                { "id": "L00", "name": "빈 무대", "class": "구도", "command": "(인물을 모두 fade_out한 채로 둔다)" },
                { "id": "X09", "name": "따로 둔 카드", "class": "구도", "command": "<<at start stage_dim far 0.5>>\n화자: 대사 …" }
              ]
            }
            """);

        return dir;
    }

    // ── 카탈로그 ────────────────────────────────────────────────────────────

    [Fact]
    public void 카드의_분류를_종류에_잇는다()
    {
        CardCatalog catalog = CardCatalog.Load(CatalogFolder());

        Assert.Equal(new[] { "E04" }, catalog.OfKind(CommandKind.Placement).Select(c => c.Id));
        Assert.Equal(new[] { "G01" }, catalog.OfKind(CommandKind.Acting).Select(c => c.Id));

        // 카드 하나만 다른 종류에 두는 칸이 분류를 이긴다.
        Assert.Equal(new[] { "B02", "X09" }, catalog.OfKind(CommandKind.Screen).Select(c => c.Id));

        // 분류가 표에 없는 카드는 어느 종류에도 오르지 않고 적힌다.
        Assert.Null(catalog.Cards.First(c => c.Id == "L00").Kind);
        Assert.Single(catalog.Problems);
        Assert.Empty(catalog.OfKind(CommandKind.Camera));
    }

    [Fact]
    public void 본보기는_대사_줄마다_묶음으로_갈린다()
    {
        Card card = CardCatalog.Load(CatalogFolder()).Cards.First(c => c.Id == "G01");

        Assert.Equal(new[] { "줄 머리", "말이 꺾이는 대목" }, card.Parts.Select(p => p.Title));
        Assert.Equal(new[] { "start motion", "start face_swap" }, card.Parts[0].Commands.Select(c => $"{c.Anchor} {c.Name}"));
        Assert.Equal("화자: 앞 마디,[beat=a/] 뒷 마디.", card.Parts[1].Example);
        Assert.Equal("a", card.Parts[1].Commands[0].Anchor);
    }

    [Fact]
    public void 채울_자리와_간격과_지문을_읽는다()
    {
        IReadOnlyList<Card> cards = CardCatalog.Load(CatalogFolder()).Cards;

        CardCommand swap = cards.First(c => c.Id == "G01").Parts[0].Commands[1];
        Assert.Equal(new[] { "s1", "표정", "0.2s" }, swap.Args.Select(a => a.Text));
        Assert.Equal(new[] { false, true, false }, swap.Args.Select(a => a.Placeholder));

        CardPart cover = cards.First(c => c.Id == "B02").Parts[0];
        Assert.Equal(new CardGap("in", "1s"), cover.Gaps[0]);
        Assert.Equal("회상 배경", cover.Commands[1].Args[1].Text);
        Assert.True(cover.Commands[1].Args[1].Placeholder);
        Assert.Equal(new[] { "(인물 등장)" }, cover.Notes);

        // 커맨드가 없는 카드는 묶음이 없다.
        Assert.Empty(cards.First(c => c.Id == "L00").Parts);
    }

    [Fact]
    public void 카탈로그가_없거나_판이_다르면_까닭을_든다()
    {
        string dir = Directory.CreateTempSubdirectory("st-catalog-").FullName;
        Assert.Throws<InvalidDataException>(() => CardCatalog.Load(dir));

        string folder = CatalogFolder();
        File.WriteAllText(Path.Combine(folder, CardCatalog.KindsFile), "{ \"format\": \"ked-catalog-kinds/9\" }");
        Assert.Throws<InvalidDataException>(() => CardCatalog.Load(folder));
    }

    // ── 카드를 펼친다 ───────────────────────────────────────────────────────

    [Fact]
    public void 카드에서_온_커맨드는_줄_끝에_출처를_든다()
    {
        EditOutcome outcome = LineEditor.Apply(Scripts.Sample, new AddCommand("t1", "start", "fade_in", ["s1", "0.6s"], Card: "E04"));

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("<<at start fade_in s1 0.6s>> // card:E04\n", outcome.Text);
        Assert.Equal("<<at start fade_in s1 0.6s>> // card:E04", outcome.Added);
        Assert.False(LineEditor.Apply(Scripts.Sample, new AddCommand("t1", "start", "fade_in", ["s1"], Card: "E04 // x")).Ok);
    }

    [Fact]
    public void 같은_카드를_한_줄에_또_펼치면_둘째부터_번호가_붙는다()
    {
        Assert.Equal("E04", LineEditor.NextCardTag(Scripts.Sample, Scripts.Read(Scripts.Sample, "t1"), "E04"));

        string once = LineEditor.Apply(Scripts.Sample, new AddCommand("t1", "start", "fade_in", ["s1"], Card: "E04")).Text;
        Assert.Equal("E04#2", LineEditor.NextCardTag(once, Scripts.Read(once, "t1"), "E04"));
        Assert.Equal("F02", LineEditor.NextCardTag(once, Scripts.Read(once, "t1"), "F02"));

        string twice = LineEditor.Apply(once, new AddCommand("t1", "end", "fade_out", ["s1"], Card: "E04#2")).Text;
        Assert.Equal("E04#3", LineEditor.NextCardTag(twice, Scripts.Read(twice, "t1"), "E04"));

        // 다른 줄의 것은 세지 않는다.
        Assert.Equal("E04", LineEditor.NextCardTag(twice, Scripts.Read(twice, "t3"), "E04"));
    }

    [Fact]
    public void 조작_여럿은_모두_받아들여져야_바뀌고_되돌리기_한_칸이다()
    {
        StagingEditor editor = new(Scripts.Sample);
        LineEdit[] card =
        [
            new AddCommand("t1", "start", "move_by", ["s1", "3.27u", "0u", "0fr"], Card: "E04"),
            new AddCommand("t1", "start", "fade_in", ["s1", "0.6s"], Card: "E04"),
            new SetGap("t1", "start", "1s"),
        ];

        EditOutcome outcome = editor.Apply(card);

        Assert.True(outcome.Changed);
        Assert.Contains(
            "<<at start face s2 05>>\n<<at start move_by s1 3.27u 0u 0fr>> // card:E04\n<<at start fade_in s1 0.6s>> // card:E04\n<<gap start 1s>>\n",
            editor.Text);
        Assert.Equal(1, editor.Revision);

        Assert.True(editor.Undo());
        Assert.Equal(Scripts.Sample, editor.Text);

        // 하나라도 거절되면 아무것도 바뀌지 않는다.
        EditOutcome refused = editor.Apply([card[0], new AddCommand("t1", "zz", "fade_in", ["s1"])]);
        Assert.False(refused.Ok);
        Assert.Contains("2번째", refused.Error);
        Assert.Equal(Scripts.Sample, editor.Text);
    }

    [Fact]
    public void 명령줄에서도_출처를_남긴다()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("st-cli-").FullName, "a.yarn");
        File.WriteAllText(path, Scripts.Sample);

        Assert.Equal(0, StCli.Run(["add", path, "t1", "end", "fade_out", "s1", "--card", "X01"], new StringWriter(), new StringWriter()));
        Assert.Contains("<<at end fade_out s1>> // card:X01\n", File.ReadAllText(path));
    }

    // ── 값의 후보 · 설정 ────────────────────────────────────────────────────

    [Fact]
    public void 값의_후보는_그_대본에_쓰인_것이고_많이_쓰인_차례다()
    {
        ValueCandidates candidates = ValueCandidates.Collect(YarnLineReader.ReadFile(Scripts.Sample).Lines);
        StageCommandSpecTable specs = StageCommandSpecTable.Default;
        specs.TryGet("face", out StageCommandSpec face);
        specs.TryGet("emoji", out StageCommandSpec emoji);

        Assert.Equal(new[] { "s2", "s1" }, candidates.For(face.Args[0]));
        Assert.Equal(new[] { "05", "03", "3" }, candidates.For(face.Args[1]));
        Assert.Equal(new[] { "star" }, candidates.For(emoji.Args[1]));

        // 길이처럼 목록에서 고르지 않는 칸에는 후보가 없다.
        specs.TryGet("face_swap", out StageCommandSpec swap);
        Assert.Empty(candidates.For(swap.Args[2]));
    }

    [Fact]
    public void 설정은_대본마다_카탈로그의_자리를_든다()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("st-set-").FullName, "settings.json");
        ToolSettings settings = new() { LastPath = "C:/대본" };
        settings.Catalogs["C:/대본"] = "C:/작업공간/catalog";
        settings.Save(path);

        Assert.Equal("C:/작업공간/catalog", ToolSettings.Load(path).Catalogs["C:/대본"]);
        Assert.Empty(new ToolSettings().Catalogs);
    }

    // ── 카탈로그 칸(K7-1) ───────────────────────────────────────────────────

    private static readonly CommandKind[] Six = CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToArray();

    [Fact]
    public void 카탈로그_칸은_그_종류의_어휘_전부와_카드를_보인다()
    {
        StageCommandSpecTable specs = StageCommandSpecTable.Default;
        CardCatalog catalog = CardCatalog.Load(CatalogFolder());

        IReadOnlyList<CatalogEntry> shown = CatalogIndex.Find(specs, catalog, CommandKind.Placement, Six, query: null);

        // 커맨드가 먼저(명세 표의 차례), 카드가 뒤(카탈로그의 차례). 바로 실행하는 명령은 앵커에 넣지 못해 빠진다.
        Assert.Equal(
            specs.OfKind(CommandKind.Placement).Where(s => !s.Immediate).Select(s => s.Name),
            shown.Where(e => !e.IsCard).Select(e => e.Id));
        Assert.Equal(new[] { "E04" }, shown.Where(e => e.IsCard).Select(e => e.Id));
        Assert.All(shown, e => Assert.Equal(CommandKind.Placement, e.Kind));

        // 카드는 ID · 이름과 펼치는 커맨드의 이름만 든다 — 카드의 글(의도 · 언제)은 들지 않는다(D17).
        CatalogEntry card = shown.Single(e => e.IsCard);
        Assert.Equal("옆에서 들어옴", card.Name);
        Assert.Equal("", card.Summary);
        Assert.Equal(new[] { "move_by", "fade_in" }, card.Fields);

        // 카탈로그를 열지 않았으면 커맨드만.
        Assert.DoesNotContain(CatalogIndex.Find(specs, null, CommandKind.Placement, Six, ""), e => e.IsCard);
    }

    [Fact]
    public void 커맨드의_칸은_이름으로_보이고_없어도_되는_칸은_괄호로_싼다()
    {
        CatalogEntry zoom = CatalogIndex.Commands(StageCommandSpecTable.Default, CommandKind.Camera).Single(e => e.Id == "shot_zoom");
        StageCommandSpec spec = StageCommandSpecTable.Default.OfKind(CommandKind.Camera).Single(s => s.Name == "shot_zoom");

        Assert.Equal("렌즈의 줌", zoom.Summary);
        Assert.Equal("줌", zoom.Fields[0]);
        Assert.Equal($"[{spec.Args[1].Label}]", zoom.Fields[1]);
        Assert.Equal(spec.Args.Count, zoom.Fields.Count);
    }

    [Fact]
    public void 찾으면_여섯_종류에서_이름과_설명으로_찾는다()
    {
        StageCommandSpecTable specs = StageCommandSpecTable.Default;
        CardCatalog catalog = CardCatalog.Load(CatalogFolder());

        // 카메라를 골라 두었어도 다른 종류의 것이 나온다 — 어느 종류인지 몰라도 찾게.
        IReadOnlyList<CatalogEntry> fade = CatalogIndex.Find(specs, catalog, CommandKind.Camera, Six, "FADE_");
        Assert.Contains(fade, e => e.Id == "fade_in" && e.Kind == CommandKind.Placement);
        Assert.All(fade, e => Assert.Contains("fade_", e.Id + e.Summary, StringComparison.OrdinalIgnoreCase));

        // 설명으로도 찾고, 낱말이 여럿이면 모두 든 것만.
        Assert.Contains(CatalogIndex.Find(specs, catalog, CommandKind.Camera, Six, "나타난다"), e => e.Id == "fade_in");
        IReadOnlyList<CatalogEntry> lensZoom = CatalogIndex.Find(specs, catalog, CommandKind.Acting, Six, "렌즈 줌");
        Assert.Contains(lensZoom, e => e.Id == "shot_zoom");
        Assert.DoesNotContain(lensZoom, e => e.Id == "shot_pan");

        // 카드는 ID와 이름으로 찾는다. 카드의 글로는 찾지 않는다(D17).
        Assert.Equal(new[] { "G01" }, CatalogIndex.Find(specs, catalog, CommandKind.Camera, Six, "g01").Select(e => e.Id));
        Assert.Equal(new[] { "E04" }, CatalogIndex.Find(specs, catalog, CommandKind.Camera, Six, "들어옴").Select(e => e.Id));
        Assert.Empty(CatalogIndex.Find(specs, catalog, CommandKind.Camera, Six, "걸어"));
    }

    // ── 카드 묶음(K7-4) ─────────────────────────────────────────────────────

    // 지어낸 대본 — E04를 온전히 · E04를 한 줄에 또(#2, 일부) · G01의 첫 묶음, 다음 줄에 G01의 둘째 묶음. 출처 없는 커맨드도 섞는다.
    internal const string CardScript =
        "title: C\n---\n" +
        "<<at start move_by s1 3.27u 0u 0fr>> // card:E04\n<<at start fade_in s1 0.6s>> // card:E04\n" +
        "<<at start motion s2 g01_hop>> // card:G01\n<<at start face_swap s2 03 0.2s>> // card:G01\n" +
        "<<at a fade_in s3 0.6s>> // card:E04#2\n<<at start face s1 05>>\n" +
        "가: 하나 [beat=a/]둘 #line:c1\n\n" +
        "<<at a motion s2 g01_hop>> // card:G01\n" +
        "나: 앞 마디,[beat=a/] 뒷 마디 #line:c2\n\n" +
        "다: 출처가 없는 줄 #line:c3\n===\n";

    private static (YarnFileRead File, LineRead Line) ReadCardLine(string lineId)
    {
        YarnFileRead file = YarnLineReader.ReadFile(CardScript, "yarn", StageCommandSpecTable.Default);
        Assert.True(file.TryGet(lineId, out LineRead line));
        return (file, line);
    }

    [Fact]
    public void 출처로_커맨드를_카드_한_장씩_묶고_카탈로그와_견준다()
    {
        CardCatalog catalog = CardCatalog.Load(CatalogFolder());
        (YarnFileRead file, LineRead c1) = ReadCardLine("c1");

        IReadOnlyList<CardGroup> cards = CardGroups.Read(CardScript, file, c1, catalog);

        // 처음 나온 차례 — 같은 카드를 또 펼친 것(#2)은 따로다. 출처 없는 face는 어느 카드에도 들지 않는다.
        Assert.Equal(new[] { "E04", "G01", "E04#2" }, cards.Select(c => c.Tag));
        Assert.Equal(new[] { "E04", "G01", "E04" }, cards.Select(c => c.CardId));
        Assert.Equal(new[] { 2, 2, 1 }, cards.Select(c => c.Commands.Count));
        Assert.Equal("옆에서 들어옴", cards[0].Name);

        // 카드의 묶음과 같으면 온전하고, 빠진 것이 있으면 일부다(U6).
        Assert.Equal(new bool?[] { true, true, false }, cards.Select(c => c.Whole));

        // 묶인 커맨드는 줄의 주소로 든다 — E04#2의 fade_in은 앵커 a의 자리와 구도 첫째다.
        Assert.Equal(new CommandAddress(c1.LineId, "a", CommandKind.Placement, 0), Assert.Single(cards[2].Commands));
    }

    [Fact]
    public void 이웃_줄에_같은_카드의_다른_묶음이_있으면_알린다()
    {
        CardCatalog catalog = CardCatalog.Load(CatalogFolder());

        (YarnFileRead file, LineRead c1) = ReadCardLine("c1");
        Assert.Equal("다음 줄", CardGroups.Read(CardScript, file, c1, catalog).Single(c => c.Tag == "G01").Elsewhere);
        Assert.Null(CardGroups.Read(CardScript, file, c1, catalog).Single(c => c.Tag == "E04").Elsewhere);

        // 다음 줄의 G01은 둘째 묶음(말이 꺾이는 대목) 하나로 온전하다 — 앞 줄에 다른 묶음이 있다.
        Assert.True(file.TryGet("c2", out LineRead c2));
        CardGroup g01 = Assert.Single(CardGroups.Read(CardScript, file, c2, catalog));
        Assert.True(g01.Whole);
        Assert.Equal("앞 줄", g01.Elsewhere);
    }

    [Fact]
    public void 카탈로그가_없으면_묶기만_하고_출처가_없는_줄에는_카드가_없다()
    {
        (YarnFileRead file, LineRead c1) = ReadCardLine("c1");

        IReadOnlyList<CardGroup> cards = CardGroups.Read(CardScript, file, c1, null);
        Assert.Equal(3, cards.Count);
        Assert.All(cards, c =>
        {
            Assert.Null(c.Name);
            Assert.Null(c.Whole);
            Assert.Null(c.Elsewhere);
        });

        (_, LineRead c3) = ReadCardLine("c3");
        Assert.Empty(CardGroups.Read(CardScript, file, c3, CardCatalog.Load(CatalogFolder())));
    }
}
