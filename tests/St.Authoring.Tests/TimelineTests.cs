using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.Authoring.Tests;

public class TimelineTests
{
    private static readonly IReadOnlySet<CommandKind> Director =
        CommandKinds.All.Where(CommandKinds.IsDirectorKind).ToHashSet();

    // 초당 20자, 대사창이 열리는 시간은 0 — 셈하기 쉬운 값이다.
    private static readonly TypewriterPacing Pacing = new() { UnitsPerSecond = 20f };

    private static LineTimeline Timeline(string text, string lineId = "t1", ClipLengths? clips = null)
        => LineTimeline.Build(Scripts.Read(text, lineId), Director, Pacing, clips ?? ClipLengths.None);

    private static CommandAddress At(string anchor, CommandKind kind, int index) => new("line:t1", anchor, kind, index);

    private static string Apply(string text, IEnumerable<LineEdit?> edits)
    {
        EditOutcome outcome = LineEditor.ApplyAll(text, edits.Select(e => e!).ToList());
        Assert.True(outcome.Ok, outcome.Error);
        return outcome.Text;
    }

    // ── 펴 보이기 ───────────────────────────────────────────────────────────

    [Fact]
    public void 앞_앵커는_간격만큼_정확하고_글에_걸린_앵커는_어림이다()
    {
        LineTimeline timeline = Timeline(Scripts.Sample);

        Assert.Equal(new[] { "in", "start", "a", "end" }, timeline.Anchors.Select(a => a.Name));

        // in은 0에 열리고, 간격 1초(기다림 0.5초보다 길다) 뒤에 start가 열린다.
        Assert.Equal(0f, timeline.Anchors[0].Open, 3);
        Assert.Equal(1f, timeline.Anchors[1].Open, 3);
        Assert.Equal(new[] { false, false, true, true }, timeline.Anchors.Select(a => a.Estimated));
        Assert.True(timeline.Anchors[2].Open > 1f);
        Assert.True(timeline.Ready >= timeline.Anchors[3].Open);
    }

    [Fact]
    public void 막대는_앵커가_열린_때에_지연을_더한_자리에_서고_pause는_막대가_아니다()
    {
        LineTimeline timeline = Timeline(Scripts.Sample);

        Assert.Equal(new[] { "fade_out", "face", "face", "face_swap", "emoji" }, timeline.Bars.Select(b => b.Name));

        TimelineBar fadeOut = timeline.Bars[0];
        Assert.Equal(0f, fadeOut.Start, 3);
        Assert.Equal(0.5f, fadeOut.Duration!.Value, 3);
        Assert.True(fadeOut.Resizable);

        // pause 15fr 뒤의 face — 0.5초 늦게 시작한다. 그 기다림은 바로 앞의 pause라 0까지 당길 수 있다.
        TimelineBar face = timeline.Bars[1];
        Assert.Equal(0.5f, face.Start, 3);
        Assert.Equal(0.5f, face.Delay, 3);
        Assert.Equal(0f, face.Floor, 3);
        Assert.Null(face.Duration);
        Assert.False(face.Resizable);

        Assert.Equal("t1/a/acting/0", timeline.Bars[3].Address.ToString());
    }

    [Fact]
    public void 종류로_거르면_그_종류의_막대만_남는다()
    {
        LineTimeline timeline = LineTimeline.Build(
            Scripts.Read(Scripts.Sample, "t1"), new HashSet<CommandKind> { CommandKind.Placement }, Pacing, ClipLengths.None);

        Assert.Equal(new[] { "fade_out" }, timeline.Bars.Select(b => b.Name));
        Assert.Equal(4, timeline.Anchors.Count);
    }

    [Fact]
    public void 모션의_길이는_대본_곁의_모션_파일에서_읽는다()
    {
        string dir = Directory.CreateTempSubdirectory("st-clips-").FullName;
        string script = Path.Combine(dir, "ep01.yarn");
        string text = Scripts.Sample.Replace("emoji s2 star", "motion s2 nod");
        File.WriteAllText(script, text);
        File.WriteAllText(Path.Combine(dir, "motions.json"), """{ "schema": "motions/2", "motions": [ { "name": "nod", "duration": 1.0 }, { "name": "hop", "duration": 0.3 } ] }""");
        File.WriteAllText(Path.Combine(dir, "ep01.motions.json"), """{ "schema": "motions/2", "motions": [ { "name": "nod", "duration": 0.4 } ] }""");

        ClipLengths clips = ClipLengths.Load(script);

        // 그 편의 파일이 모두가 쓰는 파일을 이긴다.
        Assert.Equal(2, clips.Count);
        Assert.Equal(0.4f, Timeline(text, clips: clips).Bars[^1].Duration!.Value, 3);

        // 모션 파일이 없으면 길이를 모른다 — 점으로 보인다.
        Assert.Equal(0, ClipLengths.Load(Path.Combine(Directory.CreateTempSubdirectory("st-clips-").FullName, "a.yarn")).Count);
        Assert.Null(Timeline(text).Bars[^1].Duration);
    }

    // ── 끌어 고치기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 막대를_옆으로_끌면_지연이_프레임에_맞춰_적힌다()
    {
        LineRead line = Scripts.Read(Scripts.Sample, "t1");

        // in의 face(지연 0.5초)를 0.81초로 — 가장 가까운 프레임(24fr)이다.
        string moved = Apply(Scripts.Sample, TimelineEdits.MoveBar(line, At("in", CommandKind.Acting, 0), 0.81f, alone: false));
        Assert.Contains("<<at in pause 24fr>>\n<<at in face s2 05>>", moved);

        // 같은 자리로 끌면 할 일이 없다.
        Assert.Empty(TimelineEdits.MoveBar(line, At("in", CommandKind.Acting, 0), 0.5f, alone: false));
        Assert.Empty(TimelineEdits.MoveBar(line, At("in", CommandKind.Acting, 9), 1f, alone: false));
    }

    [Fact]
    public void 더_앞선_기다림보다_당기면_거기서_멈춘다()
    {
        string text = LineEditor.Apply(Scripts.Sample, new AddCommand("t1", "in", "fade", ["s2"])).Text;
        LineRead line = Scripts.Read(text, "t1");

        // in에 붙은 fade의 바닥은 앞의 pause(0.5초)다 — 0.1초로 끌어도 0.5초에 남는다.
        Assert.Empty(TimelineEdits.MoveBar(line, At("in", CommandKind.Placement, 1), 0.1f, alone: false));
    }

    [Fact]
    public void 혼자_옮기면_뒤의_커맨드는_제자리에_남는다()
    {
        // fade_out(+0) · pause 15fr · face(+0.5) — fade_out만 0.2초로 늦춘다.
        LineRead line = Scripts.Read(Scripts.Sample, "t1");
        string moved = Apply(Scripts.Sample, TimelineEdits.MoveBar(line, At("in", CommandKind.Placement, 0), 0.2f, alone: true));

        Assert.Contains("<<at in pause 6fr>>\n<<at in fade_out s1 15fr>>\n<<at in pause 9fr>>\n<<at in face s2 05>>", moved);

        LineReadStep step = Scripts.Read(moved, "t1").Step("in");
        Assert.Equal(new[] { 0.2f, 0.5f }, step.Commands.Where(c => c.Name != "pause").Select(c => MathF.Round(c.Delay, 3)));
    }

    [Fact]
    public void 혼자_옮겨도_뒤의_커맨드를_넘어설_수는_없다_함께_밀린다()
    {
        LineRead line = Scripts.Read(Scripts.Sample, "t1");
        string moved = Apply(Scripts.Sample, TimelineEdits.MoveBar(line, At("in", CommandKind.Placement, 0), 0.8f, alone: true));

        LineReadStep step = Scripts.Read(moved, "t1").Step("in");
        Assert.Equal(new[] { 0.8f, 0.8f }, step.Commands.Where(c => c.Name != "pause").Select(c => MathF.Round(c.Delay, 3)));
    }

    [Fact]
    public void 함께_옮기면_뒤의_커맨드도_같은_만큼_움직인다()
    {
        LineRead line = Scripts.Read(Scripts.Sample, "t1");
        string moved = Apply(Scripts.Sample, TimelineEdits.MoveBar(line, At("in", CommandKind.Placement, 0), 0.2f, alone: false));

        LineReadStep step = Scripts.Read(moved, "t1").Step("in");
        Assert.Equal(new[] { 0.2f, 0.7f }, step.Commands.Where(c => c.Name != "pause").Select(c => MathF.Round(c.Delay, 3)));
    }

    [Fact]
    public void 막대의_끝을_끌면_길이_칸이_적힌다()
    {
        LineRead line = Scripts.Read(Scripts.Sample, "t1");

        string resized = Apply(Scripts.Sample, [TimelineEdits.ResizeBar(line, At("a", CommandKind.Acting, 0), 0.66f)]);
        Assert.Contains("<<at a face_swap s2 03 20fr>>", resized);

        // 길이 칸이 없는 커맨드와 같은 길이는 할 일이 없다.
        Assert.Null(TimelineEdits.ResizeBar(line, At("end", CommandKind.Acting, 0), 1f));
        Assert.Null(TimelineEdits.ResizeBar(line, At("in", CommandKind.Placement, 0), 0.5f));
    }

    [Fact]
    public void 앵커를_끌면_앞_앵커의_간격이_적히고_기다림보다_짧으면_걷힌다()
    {
        LineTimeline timeline = Timeline(Scripts.Sample);

        // start를 2초로 — in의 간격이 2초가 된다.
        string later = Apply(Scripts.Sample, [TimelineEdits.MoveAnchor(timeline, 1, 2f)]);
        Assert.Contains("<<gap in 60fr>>", later);
        Assert.Equal(2f, Timeline(later).Anchors[1].Open, 3);

        // in의 커맨드가 모두 시작되는 때(0.5초)보다 당기면 간격은 뜻이 없다 — 걷는다.
        string earlier = Apply(Scripts.Sample, [TimelineEdits.MoveAnchor(timeline, 1, 0.3f)]);
        Assert.DoesNotContain("<<gap in", earlier);
        Assert.Equal(0.5f, Timeline(earlier).Anchors[1].Open, 3);

        // 첫 앵커는 끌 것이 없고, 같은 자리는 할 일이 없다.
        Assert.Null(TimelineEdits.MoveAnchor(timeline, 0, 1f));
        Assert.Null(TimelineEdits.MoveAnchor(timeline, 1, 1f));
    }

    [Fact]
    public void 글에_걸린_앵커는_뒤로만_밀린다()
    {
        LineTimeline timeline = Timeline(Scripts.Sample);
        float natural = timeline.Anchors[2].Open;

        string later = Apply(Scripts.Sample, [TimelineEdits.MoveAnchor(timeline, 2, natural + 1f)]);
        Assert.True(Timeline(later).Anchors[2].Open > natural + 0.9f);

        // 당기면 start의 간격이 없으므로 할 일이 없다 — 타자기가 그 글자에 닿아야 열린다.
        Assert.Null(TimelineEdits.MoveAnchor(timeline, 2, 1.01f));
    }

    [Fact]
    public void 줄의_끝을_끌면_end의_간격이_적힌다()
    {
        LineTimeline timeline = Timeline(Scripts.Sample);
        float end = timeline.Anchors[^1].Open;

        string held = Apply(Scripts.Sample, [TimelineEdits.MoveReady(timeline, end + 1f)]);

        Assert.Contains("<<gap end 30fr>>", held);
        Assert.Equal(end + 1f, Timeline(held).Ready, 2);
    }

    [Fact]
    public void 시각은_프레임에_맞추고_타자기의_속도는_대본마다_적는다()
    {
        Assert.Equal(0.5f, TimelineEdits.Snap(0.51f), 4);
        Assert.Equal(0f, TimelineEdits.Snap(-1f), 4);

        ToolSettings settings = new();
        Assert.Equal(20f, settings.TypewriterSpeedFor("C:/대본"));
        settings.TypewriterSpeeds["C:/대본"] = 30f;
        Assert.Equal(30f, settings.TypewriterSpeedFor("C:/대본"));
    }
}
