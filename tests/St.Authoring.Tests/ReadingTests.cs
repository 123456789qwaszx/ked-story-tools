using Ked.Presentation.Core;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.Authoring.Tests;

public class ReadingTests
{
    private static readonly IReadOnlySet<CommandKind> Director =
        CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToHashSet();

    /// <summary>
    /// ked-story-director가 내보내는 꼴의 폴더 — 진행 파일 하나(챕터), 장면마다 yarn 파일 하나, 에피소드마다 노드 하나.
    /// 진행 파일에 없는 대본(loose.yarn)이 하나 곁들여 있다. 글은 지어낸 것이다.
    /// </summary>
    private static string StoryFolder()
    {
        string dir = Directory.CreateTempSubdirectory("st-lib-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "yarn"));

        File.WriteAllText(Path.Combine(dir, "ch01.progression.json"), """
            {
              "ChapterId": "ch01", "DisplayName": "1편 · 시험", "StartEpisodeId": "ch01_market_01", "Stats": [],
              "Nodes": [
                { "EpisodeId": "ch01_market_01", "Title": "첫 에피소드", "DialogueEntryId": "ch01_market_01", "SceneId": "market", "NextOptions": [] },
                { "EpisodeId": "ch01_welcome_01", "Title": "둘째 장면", "DialogueEntryId": "ch01_welcome_01", "SceneId": "welcome" },
                { "EpisodeId": "ch01_market_01a", "Title": "갈래", "DialogueEntryId": "ch01_market_01a", "SceneId": "market" },
                { "EpisodeId": "ch01_lost_01", "Title": "없는 노드", "DialogueEntryId": "ch01_lost_01", "SceneId": "welcome" }
              ]
            }
            """);

        File.WriteAllText(Path.Combine(dir, "yarn", "ch01_market.yarn"),
            "// 장면: 시장 (market)\n// 장 보러 나온 길\n\n// 에피소드: 첫 에피소드\ntitle: ch01_market_01\n---\n" +
            "<<at start face s1 3>>\n가: 하나 #line:m1\n나: 둘 #line:m2\n===\n\n" +
            "// 에피소드: 갈래\ntitle: ch01_market_01a\n---\n<<at start shot_zoom 1.2>>\n가: 갈래 하나 #line:m3\n===\n");

        File.WriteAllText(Path.Combine(dir, "yarn", "ch01_welcome.yarn"),
            "// 장면: 환영회 (welcome)\n\ntitle: ch01_welcome_01\n---\n다: 셋 #line:w1\n===\n");

        File.WriteAllText(Path.Combine(dir, "loose.yarn"), Scripts.Sample);
        return dir;
    }

    private static LineOutline Outline(string lineId, IReadOnlySet<CommandKind> kinds)
        => LineOutline.Build(Scripts.Sample, Scripts.Read(Scripts.Sample, lineId), kinds);

    // ── 챕터 · 장면 · 에피소드 ──────────────────────────────────────────────

    [Fact]
    public void 진행_파일이_챕터이고_SceneId가_장면이고_노드가_에피소드다()
    {
        ScriptLibrary library = ScriptLibrary.Open(StoryFolder());
        StoryChapter chapter = library.Chapters[0];

        Assert.Equal("ch01", chapter.Id);
        Assert.Equal("1편 · 시험", chapter.Name);

        // 장면은 에피소드가 처음 나오는 차례로, 이름은 파일 머리의 주석에서.
        Assert.Equal(new[] { "market", "welcome" }, chapter.Scenes.Select(s => s.Id));
        Assert.Equal(new[] { "시장", "환영회" }, chapter.Scenes.Select(s => s.Name));

        // 장면 하나에 든 에피소드가 한 번에 온다 — 진행 파일의 차례로.
        StoryScene market = chapter.Scenes[0];
        Assert.Equal(new[] { "첫 에피소드", "갈래" }, market.Episodes.Select(e => e.Title));
        Assert.Equal(new[] { "m1", "m2" }, market.Episodes[0].Lines.Select(l => l.Id));
        Assert.Equal(new[] { "m3" }, market.Episodes[1].Lines.Select(l => l.Id));
        Assert.Equal(3, market.LineCount);
    }

    [Fact]
    public void 대본에_없는_에피소드는_빈_채로_남고_표시된다()
    {
        StoryScene welcome = ScriptLibrary.Open(StoryFolder()).Chapters[0].Scenes[1];

        Assert.Equal(new[] { false, true }, welcome.Episodes.Select(e => e.Missing));
        Assert.Empty(welcome.Episodes[1].Lines);
    }

    [Fact]
    public void 진행_파일에_없는_대본은_맨_뒤의_챕터에_파일마다_장면으로_모인다()
    {
        ScriptLibrary library = ScriptLibrary.Open(StoryFolder());
        StoryChapter loose = library.Chapters[1];

        Assert.Equal(2, library.Chapters.Count);
        Assert.Equal(ScriptLibrary.LooseChapterId, loose.Id);
        Assert.Equal(new[] { "loose" }, loose.Scenes.Select(s => s.Name));
        Assert.Equal(new[] { "T" }, loose.Scenes[0].Episodes.Select(e => e.NodeName));
        Assert.Equal(new[] { "t1", "t2", "t3", "t4", null }, loose.Scenes[0].Episodes[0].Lines.Select(l => l.Id));
        Assert.Equal(9, library.LineCount);
        Assert.Empty(library.Problems);
    }

    [Fact]
    public void 진행_파일이_없는_폴더와_파일_하나는_챕터_하나로_열린다()
    {
        string dir = Directory.CreateTempSubdirectory("st-lib-").FullName;
        string path = Path.Combine(dir, "a.yarn");
        File.WriteAllText(path, Scripts.Sample);

        foreach (string open in new[] { dir, path })
        {
            ScriptLibrary library = ScriptLibrary.Open(open);

            Assert.Single(library.Chapters);
            Assert.Equal("대본", library.Chapters[0].Name);
            Assert.Equal(5, library.Chapters[0].Scenes[0].LineCount);
        }
    }

    [Fact]
    public void 장면_안에서_대사_글과_화자와_LineId로_찾는다()
    {
        StoryScene market = ScriptLibrary.Open(StoryFolder()).Chapters[0].Scenes[0];

        Assert.Equal(new[] { "m1", "m3" }, Ids(market.Search("하나")));
        Assert.Equal(new[] { "m3" }, Ids(market.Search("가 갈래")));
        Assert.Equal(new[] { "m2" }, Ids(market.Search("M2")));
        Assert.Empty(market.Search("없는말"));

        // 거르는 것이 없으면 에피소드가 모두 남고, 있으면 줄이 남은 에피소드만 남는다.
        Assert.Equal(2, market.Search("  ").Count);
        Assert.Equal(new[] { "갈래" }, market.Search("갈래").Select(e => e.Title));
    }

    [Fact]
    public void 종류가_있는_줄만_남기고_에피소드_하나만_볼_수_있다()
    {
        StoryScene market = ScriptLibrary.Open(StoryFolder()).Chapters[0].Scenes[0];

        Assert.Equal(new[] { "m3" }, Ids(market.Search(null, new HashSet<CommandKind> { CommandKind.Camera })));
        Assert.Equal(new[] { "m1" }, Ids(market.Search(null, new HashSet<CommandKind> { CommandKind.Acting })));
        Assert.Equal(new[] { "m1", "m2" }, Ids(market.Search(null, null, "ch01_market_01")));
        Assert.Empty(market.Search("갈래", null, "ch01_market_01"));
    }

    [Fact]
    public void 줄은_종류별_수와_고칠_수_있는가를_안다()
    {
        IReadOnlyList<LibraryLine> lines = ScriptLibrary.Open(StoryFolder()).Chapters[1].Scenes[0].Episodes[0].Lines;

        Assert.Equal(1, lines[0].Count(CommandKind.Placement));
        Assert.Equal(4, lines[0].Count(CommandKind.Acting));
        Assert.Equal(0, lines[0].Count(CommandKind.Camera));
        Assert.True(lines[0].Editable);
        Assert.False(lines[1].Editable);
    }

    [Fact]
    public void 읽지_못한_파일과_깨진_진행_파일은_건너뛰고_적는다()
    {
        string dir = StoryFolder();
        File.WriteAllBytes(Path.Combine(dir, "b.yarn"), [0x74, 0xFF, 0xFE]);
        File.WriteAllText(Path.Combine(dir, "ch02.progression.json"), "{ 깨진");

        ScriptLibrary library = ScriptLibrary.Open(dir);

        Assert.Equal(3, library.Files.Count);
        Assert.Equal(2, library.Chapters.Count);
        Assert.Equal(2, library.Problems.Count);
    }

    private static string?[] Ids(IReadOnlyList<StoryEpisode> episodes) => episodes.SelectMany(e => e.Lines).Select(l => l.Id).ToArray();

    // ── 스텝 목록 ───────────────────────────────────────────────────────────

    [Fact]
    public void 앵커는_커맨드가_없어도_모두_선다()
    {
        LineOutline outline = Outline("t1", Director);

        Assert.Equal(new[] { "in", "start", "a", "b", "end" }, outline.Steps.Select(s => s.Anchor));
        Assert.Equal(
            new[] { LineAnchorKind.Pre, LineAnchorKind.Start, LineAnchorKind.Text, LineAnchorKind.Text, LineAnchorKind.End },
            outline.Steps.Select(s => s.Kind));
        Assert.Empty(outline.Steps[3].Commands);
        Assert.True(outline.Editable);
    }

    [Fact]
    public void pause는_줄로_보이지_않고_뒤_커맨드의_지연으로_보인다()
    {
        OutlineStep step = Outline("t1", Director).Steps[0];

        Assert.Equal(new[] { "fade_out", "face" }, step.Commands.Select(c => c.Name));
        Assert.Equal(0.5f, step.Commands[1].Delay, 3);
        Assert.Equal(1f, step.Gap);
        Assert.Empty(step.Others);
    }

    [Fact]
    public void 고르지_않은_종류는_수로만_남는다()
    {
        OutlineStep step = Outline("t1", new HashSet<CommandKind> { CommandKind.Acting }).Steps[0];

        Assert.Equal(new[] { "face" }, step.Commands.Select(c => c.Name));
        Assert.Equal(0.5f, step.Commands[0].Delay, 3);
        Assert.Equal(1, step.Others[CommandKind.Placement]);
        Assert.Equal("t1/in/acting/0", step.Commands[0].Address.ToString());
    }

    [Fact]
    public void 칸은_보이는_이름과_적었는가를_든다()
    {
        string text = Scripts.Sample.Replace("face_swap s2 03 13fr", "face_swap s2 03");
        OutlineCommand command = LineOutline.Build(text, Scripts.Read(text, "t1"), Director).Steps[2].Commands[0];

        Assert.Equal(new[] { "슬롯 s2", "표정 03", "길이 13fr" }, command.Args.Select(a => $"{a.Label} {a.Value}"));
        Assert.Equal(new[] { true, true, false }, command.Args.Select(a => a.Written));
    }

    [Fact]
    public void 옛_표기_줄은_읽기만이고_까닭이_있다()
    {
        LineOutline outline = Outline("t2", Director);

        Assert.False(outline.Editable);
        Assert.Contains("옛 표기", outline.ReadOnlyReason);
        Assert.Equal(1, outline.ShownCommands);
    }

    [Theory]
    [InlineData(0.5f, "0.5s · 15fr")]
    [InlineData(1f, "1s · 30fr")]
    [InlineData(0.37f, "0.37s")]
    [InlineData(0f, "0s")]
    public void 시간은_프레임에_맞으면_프레임을_함께_적는다(float seconds, string expected)
        => Assert.Equal(expected, TimeText.Format(seconds));

    // ── 설정 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 설정은_없으면_기본값이고_쓰면_다시_읽힌다()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("st-set-").FullName, "sub", "settings.json");

        Assert.Null(ToolSettings.Load(path).LastPath);

        new ToolSettings { LastPath = "C:/대본" }.Save(path);
        Assert.Equal("C:/대본", ToolSettings.Load(path).LastPath);

        File.WriteAllText(path, "{ 깨진");
        ToolSettings broken = ToolSettings.Load(path);
        Assert.Null(broken.LastPath);
        Assert.NotNull(broken.LoadProblem);
    }
}
