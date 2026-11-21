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
}
