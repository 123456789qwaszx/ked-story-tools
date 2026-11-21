using Ked.Presentation.Core;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.Authoring.Tests;

public class ReadingTests
{
    private static readonly IReadOnlySet<CommandKind> Director =
        CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToHashSet();

    private static ScriptLibrary Library()
    {
        string dir = Directory.CreateTempSubdirectory("st-lib-").FullName;
        File.WriteAllText(Path.Combine(dir, "a.yarn"), Scripts.Sample);
        File.WriteAllText(Path.Combine(dir, "b.yarn"), "title: U\n---\n<<at start shot_zoom 1.2>>\n마: 다른 파일의 줄 #line:u1\n===\n");
        return ScriptLibrary.Open(dir);
    }

    private static LineOutline Outline(string lineId, IReadOnlySet<CommandKind> kinds)
        => LineOutline.Build(Scripts.Sample, Scripts.Read(Scripts.Sample, lineId), kinds);

    // ── 줄 찾기 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 폴더의_줄을_파일_순으로_모은다()
    {
        ScriptLibrary library = Library();

        Assert.Equal(2, library.Files.Count);
        Assert.Equal(new[] { "t1", "t2", "t3", "t4", null, "u1" }, library.Lines.Select(l => l.Id));
        Assert.Empty(library.Problems);
    }

    [Fact]
    public void 대사_글과_화자와_LineId로_찾는다()
    {
        ScriptLibrary library = Library();

        Assert.Equal(new[] { "t3" }, library.Search("들여쓴").Select(l => l.Id));
        Assert.Equal(new[] { "t1", "t2", "t3" }, library.Search("나").Select(l => l.Id));
        Assert.Equal(new[] { "u1" }, library.Search("U1").Select(l => l.Id));
        Assert.Equal(new[] { "t2" }, library.Search("나 옛").Select(l => l.Id));
        Assert.Equal(6, library.Search("  ").Count);
        Assert.Empty(library.Search("없는말"));
    }

    [Fact]
    public void 종류가_있는_줄만_남긴다()
    {
        ScriptLibrary library = Library();

        Assert.Equal(new[] { "u1" }, library.Search(null, new HashSet<CommandKind> { CommandKind.Camera }).Select(l => l.Id));
        Assert.Equal(new[] { "t1", "t2" }, library.Search(null, new HashSet<CommandKind> { CommandKind.Placement }).Select(l => l.Id));
        Assert.Equal(new[] { "t1", "t3" }, library.Search(null, new HashSet<CommandKind> { CommandKind.Acting }).Select(l => l.Id));
    }

    [Fact]
    public void 줄은_종류별_수와_고칠_수_있는가를_안다()
    {
        LibraryLine line = Library().Lines[0];

        Assert.Equal(1, line.Count(CommandKind.Placement));
        Assert.Equal(4, line.Count(CommandKind.Acting));
        Assert.Equal(0, line.Count(CommandKind.Camera));
        Assert.True(line.Editable);
        Assert.False(Library().Lines[1].Editable);
    }

    [Fact]
    public void 읽지_못한_파일은_건너뛰고_적는다()
    {
        string dir = Directory.CreateTempSubdirectory("st-lib-").FullName;
        File.WriteAllText(Path.Combine(dir, "a.yarn"), Scripts.Sample);
        File.WriteAllBytes(Path.Combine(dir, "b.yarn"), [0x74, 0xFF, 0xFE]);

        ScriptLibrary library = ScriptLibrary.Open(dir);

        Assert.Single(library.Files);
        Assert.Single(library.Problems);
    }

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
