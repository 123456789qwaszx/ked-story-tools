using Avalonia;
using Ked.Presentation.Core;
using St.App.Views;
using St.Authoring.Reading;

namespace St.App.Tests;

/// <summary>타임라인(K4-4) — 자리 셈(화면 없이)과, 끌어 고친 것이 파일에 쓰이는 것.</summary>
public class TimelineTests
{
    // 지어낸 대본. in: fade_out(0.5초) · 기다림 0.5초 · face — 간격 1초. start: 겹치는 둘. a: face_swap.
    private const string Script =
        "title: T\n---\n" +
        "<<at in fade_out s1 15fr>>\n<<at in pause 15fr>>\n<<at in face s2 05>>\n<<gap in 1s>>\n" +
        "<<at start move_by s1 1u 0u 30fr>>\n<<at start fade_in s1 15fr>>\n<<at start shot_zoom 1.2 0.4s>>\n" +
        "<<at a face_swap s2 03>>\n<<at a motion s2 nod>>\n" +
        "가: [beat=in/]하나 둘 셋 넷 [beat=a/]다섯 여섯 #line:t1\n\n" +
        "<<at start fade_in s1 10fr>>\n<<fade_out s2 10fr>>\n나: 실행되지 않는 명령 #line:t2\n===\n";

    private static readonly CommandKind[] Tracks = MainWindow.DirectorKinds;

    private static LineTimeline Timeline()
        => LineTimeline.Build(
            YarnLineReader.Read(Script, "t1"), Tracks.ToHashSet(), new TypewriterPacing { UnitsPerSecond = 20f }, MotionLibrary.None);

    private static (MainWindow Window, string Path) Open()
    {
        string dir = Directory.CreateTempSubdirectory("st-timeline-").FullName;
        string path = Path.Combine(dir, "a.yarn");
        File.WriteAllText(path, Script);
        File.WriteAllText(Path.Combine(dir, "motions.json"), """{ "schema": "motions/2", "motions": [ { "name": "nod", "duration": 0.5 } ] }""");

        MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-timeline-set-").FullName, "settings.json"));
        window.Show();
        window.OpenPath(dir);
        window.SelectLine("t1");
        return (window, path);
    }

    // ── 자리 셈 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 시각과_가로_좌표가_오간다()
    {
        TimelineLayout layout = new(Timeline(), Tracks, 1000);

        Assert.Equal(TimelineLayout.LabelWidth, layout.X(0f), 3);
        Assert.Equal(1.5f, layout.Seconds(layout.X(1.5f)), 3);
        Assert.Equal(0f, layout.Seconds(10), 3);

        // 줄의 길이가 폭 안에 들어온다.
        Assert.True(layout.X(layout.Timeline.End) <= 1000 - TimelineLayout.RightMargin + 0.001);
    }

    [Fact]
    public void 같은_트랙에서_겹치는_막대는_줄을_나눠_선다()
    {
        LineTimeline timeline = Timeline();
        TimelineLayout layout = new(timeline, Tracks, 1000);

        // start의 move_by(1초)와 fade_in(0.5초)은 같은 때에 시작하는 자리와 구도다.
        int moveBy = timeline.Bars.ToList().FindIndex(b => b.Name == "move_by");
        int fadeIn = timeline.Bars.ToList().FindIndex(b => b.Name == "fade_in");

        Assert.Equal(layout.BarRect(moveBy).Left, layout.BarRect(fadeIn).Left, 3);
        Assert.NotEqual(layout.BarRect(moveBy).Top, layout.BarRect(fadeIn).Top);
        Assert.False(layout.BarRect(moveBy).Intersects(layout.BarRect(fadeIn)));

        // 트랙은 서로 겹치지 않고, 막대는 제 종류의 트랙 안에 선다.
        (double top, double height) = layout.Track(CommandKind.Placement);
        Assert.True(layout.BarRect(moveBy).Top >= top && layout.BarRect(moveBy).Bottom <= top + height);
        Assert.True(layout.Track(CommandKind.Camera).Top + layout.Track(CommandKind.Camera).Height <= top);
    }

    [Fact]
    public void 길이를_모르는_커맨드는_점의_폭으로_서고_끝을_잡을_수_없다()
    {
        LineTimeline timeline = Timeline();
        TimelineLayout layout = new(timeline, Tracks, 1000);
        int face = timeline.Bars.ToList().FindIndex(b => b.Name == "face");
        Rect rect = layout.BarRect(face);

        Assert.Equal(TimelineLayout.PointWidth, rect.Width, 3);
        Assert.Equal(new TimelineHit(TimelineHitKind.Bar, face), layout.HitTest(new Point(rect.Right - 1, rect.Center.Y)));
    }

    [Fact]
    public void 막대의_몸과_끝과_세로줄을_가려_잡는다()
    {
        LineTimeline timeline = Timeline();
        TimelineLayout layout = new(timeline, Tracks, 1000);
        int fadeOut = timeline.Bars.ToList().FindIndex(b => b.Name == "fade_out");
        Rect rect = layout.BarRect(fadeOut);

        Assert.Equal(new TimelineHit(TimelineHitKind.Bar, fadeOut), layout.HitTest(new Point(rect.Left + 3, rect.Center.Y)));
        Assert.Equal(new TimelineHit(TimelineHitKind.BarEnd, fadeOut), layout.HitTest(new Point(rect.Right - 2, rect.Center.Y)));

        // 눈금에서는 세로줄만 잡힌다 — start(둘째 앵커). 첫 앵커는 끌 것이 없다.
        Assert.Equal(new TimelineHit(TimelineHitKind.Anchor, 1), layout.HitTest(new Point(layout.X(timeline.Anchors[1].Open), 5)));
        Assert.Equal(TimelineHit.None, layout.HitTest(new Point(layout.X(timeline.Anchors[0].Open), 5)));
        Assert.Equal(TimelineHitKind.Ready, layout.HitTest(new Point(layout.X(timeline.Ready), 5)).Kind);
        Assert.Equal(TimelineHit.None, layout.HitTest(new Point(layout.X(0.5f), 5)));
    }

    [Fact]
    public void 켜지지_않은_종류의_막대는_자리가_없다()
    {
        LineTimeline timeline = Timeline();
        TimelineLayout layout = new(timeline, [CommandKind.Acting], 1000);
        int fadeOut = timeline.Bars.ToList().FindIndex(b => b.Name == "fade_out");

        Assert.Equal(0, layout.BarRect(fadeOut).Width, 3);
        Assert.True(layout.Height < new TimelineLayout(timeline, Tracks, 1000).Height);
    }

    // ── 화면 ────────────────────────────────────────────────────────────────

    [Fact]
    public void 줄을_고르면_타임라인이_서고_모션의_길이를_곁의_파일에서_읽는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _) = Open();
            LineTimeline timeline = window.ShownTimeline!;

            Assert.Equal(new[] { "in", "start", "a", "end" }, timeline.Anchors.Select(a => a.Name));
            Assert.Equal(1f, timeline.Anchors[1].Open, 3);
            Assert.Equal(0.5f, timeline.Bars.First(b => b.Name == "motion").Duration!.Value, 3);
            Assert.Equal(20f, window.TypewriterSpeed);

            window.SelectCommand("t1/start/placement/0");
            RenderShots.Save(window, "k4-4-timeline");
            window.Close();
        });
    }

    [Fact]
    public void 막대를_끌면_그_막대만_옮겨지고_파일에_쓰인다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();

            // in의 fade_out을 0.2초 늦춘다 — 뒤의 face는 0.5초에 남는다.
            Assert.True(window.DragBar("t1/in/placement/0", 0.2f)!.Changed);

            Assert.Contains("<<at in pause 6fr>>\n<<at in fade_out s1 15fr>>\n<<at in pause 9fr>>\n<<at in face s2 05>>", File.ReadAllText(path));
            Assert.Equal(0.5f, window.ShownTimeline!.Bars.First(b => b.Name == "face").Delay, 3);
            Assert.Equal("t1/in/placement/0", window.PickedCommand?.ToString());

            // 조작은 둘이지만 되돌리기는 한 칸이다.
            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(path));
            Assert.False(window.Undo());
            window.Close();
        });
    }

    [Fact]
    public void Shift로_끌면_뒤의_커맨드도_함께_움직인다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _) = Open();

            window.DragBar("t1/in/placement/0", 0.2f, together: true);

            Assert.Equal(0.7f, window.ShownTimeline!.Bars.First(b => b.Name == "face").Delay, 3);
            window.Close();
        });
    }

    [Fact]
    public void 막대의_끝을_끌면_길이가_적히고_앵커를_끌면_간격이_적힌다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();

            window.DragBar("t1/in/placement/0", 0.5f, end: true);
            Assert.Contains("<<at in fade_out s1 30fr>>", File.ReadAllText(path));

            // start를 1초 뒤로 — in의 간격이 2초가 된다.
            window.DragAnchor("start", 1f);
            Assert.Contains("<<gap in 60fr>>", File.ReadAllText(path));
            Assert.Equal(2f, window.ShownTimeline!.Anchors[1].Open, 3);

            // 줄의 끝을 1초 뒤로 — 넘김 전에 머문다.
            float ready = window.ShownTimeline.Ready;
            window.DragAnchor(null, 1f);
            Assert.Contains("<<gap end 30fr>>", File.ReadAllText(path));
            Assert.Equal(ready + 1f, window.ShownTimeline!.Ready, 2);
            window.Close();
        });
    }

    [Fact]
    public void 움직이지_않는_끌기는_아무것도_쓰지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();

            Assert.Null(window.DragBar("t1/in/placement/0", 0f));
            Assert.Null(window.DragBar("t1/in/acting/0", 0.5f, end: true));
            Assert.Null(window.DragAnchor("in", 1f));
            Assert.Equal(Script, File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 타자기의_속도를_바꾸면_글에_걸린_앵커가_따라_움직이고_대본에_기억된다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _) = Open();
            float before = window.ShownTimeline!.Anchors[2].Open;

            window.SetTypewriterSpeed(40f);

            Assert.True(window.ShownTimeline!.Anchors[2].Open < before);
            Assert.Equal(1f, window.ShownTimeline.Anchors[1].Open, 3);
            Assert.Equal(40f, window.TypewriterSpeed);
            window.Close();
        });
    }

    [Fact]
    public void 종류를_끄면_그_트랙의_막대가_빠진다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _) = Open();

            window.SetKinds(CommandKind.Acting);

            Assert.Equal(new[] { "face", "face_swap", "motion" }, window.ShownTimeline!.Bars.Select(b => b.Name));
            window.Close();
        });
    }

    [Fact]
    public void 모션을_고르면_그_커브가_타임라인_아래에_펴진다()
    {
        HeadlessUi.Run(() =>
        {
            string dir = Directory.CreateTempSubdirectory("st-motion-").FullName;
            File.WriteAllText(Path.Combine(dir, "a.yarn"), "title: A\n---\n<<at start face s1 3>>\n<<at start pause 6fr>>\n<<at start motion s1 hop>>\n가: 하나 둘 셋 넷 다섯 #line:t1\n===\n");
            File.WriteAllText(Path.Combine(dir, "motions.json"), """
                { "schema": "motions/2", "motions": [ { "name": "hop", "rig": "character", "duration": 0.5, "end": "restore", "channels": [
                  { "node": "CharacterPortrait_Track_Move", "kind": "position",
                    "x": [ { "t": 0, "v": 0, "inTangent": 0, "outTangent": 40 }, { "t": 0.5, "v": 12, "inTangent": 0, "outTangent": 0 } ],
                    "y": [ { "t": 0, "v": 0, "inTangent": 0, "outTangent": 0 }, { "t": 0.25, "v": 60, "inTangent": 0, "outTangent": 0 }, { "t": 0.5, "v": 0, "inTangent": 0, "outTangent": 0 } ] } ] } ] }
                """);

            MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-motion-set-").FullName, "settings.json"));
            window.Show();
            window.OpenPath(dir);
            window.SelectLine("t1");
            Assert.Null(window.ShownMotion);

            Assert.True(window.SelectCommand("t1/start/acting/1"));

            Assert.Equal("hop", window.ShownMotion!.Clip.Name);
            Assert.Equal(2, window.ShownMotion.Curves.Count);
            RenderShots.Save(window, "k6-1-motion");

            // 모션이 아닌 것을 고르면 걷힌다.
            window.SelectCommand("t1/start/acting/0");
            Assert.Null(window.ShownMotion);
            window.Close();
        });
    }

    [Fact]
    public void 함께_쓰는_모션을_이_줄의_것으로_풀고_키를_솎는다()
    {
        HeadlessUi.Run(() =>
        {
            // 프레임마다 구운 호 하나를 두 줄이 함께 쓴다.
            string keys = string.Join(", ", Enumerable.Range(0, 16).Select(i =>
            {
                double Value(int n) => 60 * Math.Sin(Math.PI * n / 15);
                double slope = i is 0 or 15 ? 0 : (Value(i + 1) - Value(i - 1)) * 15;
                return FormattableString.Invariant($"{{ \"t\": {i / 30.0:0.####}, \"v\": {Value(i):0.##}, \"inTangent\": {slope:0.##}, \"outTangent\": {slope:0.##} }}");
            }));

            string dir = Directory.CreateTempSubdirectory("st-unpack-").FullName;
            string script = Path.Combine(dir, "a.yarn");
            string motions = Path.Combine(dir, "a.motions.json");
            string yarn = "title: A\n---\n<<at start motion s1 hop>>\n가: 하나 둘 셋 넷 다섯 #line:t1\n\n<<at start motion s2 hop>>\n나: 여섯 #line:t2\n===\n";
            File.WriteAllText(script, yarn);
            File.WriteAllText(motions, "{\n \"schema\": \"motions/2\",\n \"motions\": [\n  { \"name\": \"hop\", \"rig\": \"character\", \"duration\": 0.5, \"end\": \"restore\", \"channels\": [ { \"node\": \"N\", \"kind\": \"position\", \"y\": [ " + keys + " ] } ] }\n ]\n}\n");
            string before = File.ReadAllText(motions);

            MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-unpack-set-").FullName, "settings.json"));
            window.Show();
            window.OpenPath(dir);
            window.SelectLine("t1");
            window.SelectCommand("t1/start/acting/0");
            Assert.Equal(2, window.ShownMotionUses);

            Assert.True(window.UnpackPickedMotion()!.Changed);

            // 이 줄만 새 이름을 가리킨다 — 커브는 같고, 이제 이 줄만 쓴다.
            Assert.Equal(yarn.Replace("motion s1 hop", "motion s1 hop_t1"), File.ReadAllText(script));
            Assert.Equal("hop_t1", window.ShownMotion!.Clip.Name);
            Assert.Equal(1, window.ShownMotionUses);
            Assert.Equal(16, window.ShownMotion.Curves[0].Keys.Length);

            Assert.True(window.SimplifyPickedMotion()!.Changed);
            Assert.InRange(window.ShownMotion!.Curves[0].Keys.Length, 3, 7);
            RenderShots.Save(window, "k6-2-unpacked");

            // 둘 다 되돌린다 — 모션 파일도 대본도 처음 그대로.
            Assert.True(window.Undo());
            Assert.Equal(16, window.ShownMotion!.Curves[0].Keys.Length);
            Assert.True(window.Undo());
            Assert.Equal(yarn, File.ReadAllText(script));
            Assert.Equal(before, File.ReadAllText(motions));
            Assert.Equal("hop", window.ShownMotion!.Clip.Name);
            window.Close();
        });
    }

    [Fact]
    public void 이_편의_모션은_그래프에서_키를_고치고_되돌린다()
    {
        HeadlessUi.Run(() =>
        {
            string dir = Directory.CreateTempSubdirectory("st-graph-").FullName;
            string script = Path.Combine(dir, "a.yarn");
            string motions = Path.Combine(dir, "a.motions.json");
            File.WriteAllText(script, "title: A\n---\n<<at start motion s1 hop>>\n가: 하나 둘 셋 넷 다섯 #line:t1\n===\n");
            File.WriteAllText(motions, """
                {
                 "schema": "motions/2",
                 "motions": [
                  { "name": "hop", "rig": "character", "duration": 0.5, "end": "restore", "channels": [
                    { "node": "N", "kind": "position",
                      "y": [ { "t": 0, "v": 0, "inTangent": 0, "outTangent": 0 }, { "t": 0.25, "v": 60, "inTangent": 0, "outTangent": 0 }, { "t": 0.5, "v": 0, "inTangent": 0, "outTangent": 0 } ] } ] }
                 ]
                }
                """);
            string before = File.ReadAllText(motions);

            MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-graph-set-").FullName, "settings.json"));
            window.Show();
            window.OpenPath(dir);
            window.SelectLine("t1");
            Assert.Null(window.GraphMotion);

            window.SelectCommand("t1/start/acting/0");

            // 이 편의 파일에 든 모션이다 — 그래프가 그 모션에 묶여 선다.
            Assert.Equal("hop", window.GraphMotion);
            Assert.Equal("hop", window.MotionGraph.MotionName);
            Assert.Single(window.MotionGraph.Curves);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            RenderShots.Save(window, "k6-4-graph");

            // 그래프가 하듯 가운데 키를 고르고 지운다 — 파일에 쓰이고, 되돌리기가 켜진다.
            Mt.Authoring.Graph.CurveRef y = window.MotionGraph.Curves[0].Curve;
            window.MotionGraph.Select([new Mt.Authoring.Graph.KeyRef(y, 1)], additive: false);
            window.MotionGraph.DeleteSelection();

            Assert.DoesNotContain("\"v\": 60", File.ReadAllText(motions));
            Assert.Contains("모션 파일에 썼다", window.StatusLine);
            Assert.Equal(2, y.Resolve(window.MotionGraph.Motion!)!.Keys.Count);

            Assert.True(window.Undo());
            Assert.Equal(before, File.ReadAllText(motions));
            Assert.Equal(3, y.Resolve(window.MotionGraph.Motion!)!.Keys.Count);
            Assert.Equal(3, window.ShownMotion!.Curves[0].Keys.Length);

            // 도구와 부드럽게 끌기 — 단추가 그래프의 것을 바꾼다.
            window.SetGraphTool(Mt.App.Views.GraphTool.Scale);
            window.SetSoftDrag(true);
            Assert.Equal(Mt.App.Views.GraphTool.Scale, window.MotionGraph.Tool);
            Assert.True(window.MotionGraph.SoftDrag);
            RenderShots.Save(window, "k6-5-graph-tools");

            // 모션이 아닌 것을 고르면 그래프는 걷힌다.
            window.ClearPicks();
            Assert.Null(window.GraphMotion);
            window.Close();
        });
    }
}
