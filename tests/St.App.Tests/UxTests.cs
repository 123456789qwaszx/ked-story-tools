using Avalonia.Input;
using St.Authoring.Settings;

namespace St.App.Tests;

/// <summary>손에 붙는 것들(K4-5) — 여럿 고르기 · 빼기 · 복사 · 끌어 고치기 · 맞대기 · 단축키 · 접기 · 넓히기 · 창의 꼴.</summary>
public class UxTests
{
    // 지어낸 대본 — 노드 둘(에피소드 둘), 새 표기의 줄 셋.
    private const string Script =
        "title: A\n---\n" +
        "<<at in fade_out s1 15fr>>\n<<at in face s2 05>>\n" +
        "<<at start size s1 front 10fr>>\n<<at start motion s2 nod>>\n<<at a face_swap s2 03>>\n" +
        "가: [beat=in/]하나 [beat=a/]둘 #line:t1\n\n" +
        "나: 커맨드가 없는 줄 #line:t2\n\n" +
        "<<at start face s1 3>>\n다: [beat=k/]셋 #line:t3\n===\n" +
        "title: B\n---\n<<at start face s1 4>>\n라: 넷 #line:u1\n===\n";

    private static (MainWindow Window, string Path, string Settings) Open(string? settings = null)
    {
        string dir = Directory.CreateTempSubdirectory("st-ux-").FullName;
        string path = Path.Combine(dir, "a.yarn");
        File.WriteAllText(path, Script);

        settings ??= Path.Combine(Directory.CreateTempSubdirectory("st-ux-set-").FullName, "settings.json");
        MainWindow window = new(settings);
        window.Show();
        window.OpenPath(dir);
        window.SelectLine("t1");
        return (window, path, settings);
    }

    [Fact]
    public void Ctrl로_여럿을_고르고_Delete로_한꺼번에_뺀다_되돌리기는_한_칸이다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, _) = Open();

            Assert.True(window.SelectCommand("t1/in/placement/0"));
            Assert.True(window.SelectCommand("t1/a/acting/0", KeyModifiers.Control));
            Assert.Equal(2, window.PickedCommands.Count);
            Assert.Empty(window.InspectorFields);
            RenderShots.Save(window, "k4-5-many");

            // Ctrl로 다시 누르면 빠진다.
            window.SelectCommand("t1/a/acting/0", KeyModifiers.Control);
            Assert.Single(window.PickedCommands);
            window.SelectCommand("t1/a/acting/0", KeyModifiers.Control);

            Assert.True(window.PressKey(Key.Delete));

            string text = File.ReadAllText(path);
            Assert.DoesNotContain("fade_out", text);
            Assert.DoesNotContain("face_swap", text);
            Assert.Contains("<<at in face s2 05>>", text);

            Assert.True(window.PressKey(Key.Z, KeyModifiers.Control));
            Assert.Equal(Script, File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void Shift로_사이를_고른다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();

            window.SelectCommand("t1/in/placement/0");
            window.SelectCommand("t1/start/acting/0", KeyModifiers.Shift);

            // fade_out · face · size · motion — 화면의 차례로 넷, 마지막에 누른 것이 칸으로 설 것이다.
            Assert.Equal(4, window.PickedCommands.Count);
            Assert.Equal("t1/start/acting/0", window.PickedCommand?.ToString());

            Assert.True(window.PressKey(Key.Escape));
            Assert.Empty(window.PickedCommands);
            Assert.False(window.PressKey(Key.Escape));
            window.Close();
        });
    }

    [Fact]
    public void 복사한_커맨드를_다른_줄에_붙인다_같은_이름의_앵커에_없으면_start에()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, _) = Open();

            window.SelectCommand("t1/start/acting/0");
            window.SelectCommand("t1/a/acting/0", KeyModifiers.Control);
            Assert.True(window.PressKey(Key.C, KeyModifiers.Control));
            Assert.Equal(2, window.ClipboardCount);

            Assert.True(window.SelectLine("t3"));
            Assert.True(window.PressKey(Key.V, KeyModifiers.Control));

            // t3에는 a 앵커가 없다 — 둘 다 start에 선다. t1은 그대로다.
            string text = File.ReadAllText(path);
            string t3 = text[text.IndexOf("#line:t2", StringComparison.Ordinal)..text.IndexOf("#line:t3", StringComparison.Ordinal)];
            Assert.Contains("<<at start motion s2 nod>>", t3);
            Assert.Contains("<<at start face_swap s2 03>>", t3);
            RenderShots.Save(window, "k4-5-pasted");

            // 붙이기 하나가 되돌리기 한 칸이다.
            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(path));

            // 글 칸에 적는 동안에는 글 칸의 것이다 — 가로채지 않는다.
            Assert.False(window.PressKey(Key.V, KeyModifiers.Control, inText: true));
            Assert.Equal(Script, File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 칸의_이름을_끌면_값이_한_프레임씩_바뀐다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path, _) = Open();
            window.SelectCommand("t1/in/placement/0");

            Assert.True(window.ScrubField("길이", 3));
            Assert.Contains("<<at in fade_out s1 18fr>>", File.ReadAllText(path));

            Assert.True(window.ScrubField("지연", 2));
            Assert.Contains("<<at in pause 2fr>>", File.ReadAllText(path));

            // 글인 칸은 끌 수 없다.
            Assert.False(window.ScrubField("슬롯", 1));
            window.Close();
        });
    }

    [Fact]
    public void 고친_줄은_처음과_맞대어_본다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();
            window.SelectCommand("t1/a/acting/0");
            window.EditField("표정", "07");

            window.SetComparing(true);

            Assert.Contains(window.ShownChanges, c => !c.Added && c.Text.Contains("03", StringComparison.Ordinal));
            Assert.Contains(window.ShownChanges, c => c.Added && c.Text.Contains("07", StringComparison.Ordinal));
            RenderShots.Save(window, "k4-5-compare");

            // 다른 줄로 가면 닫힌다.
            window.SelectLine("t3");
            Assert.False(window.Comparing);
            Assert.Empty(window.ShownChanges);
            window.Close();
        });
    }

    [Fact]
    public void Alt와_화살표로_줄을_옮기고_Shift를_더하면_연출이_있는_줄로_건너뛴다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();

            Assert.True(window.PressKey(Key.Down, KeyModifiers.Alt));
            Assert.Equal("line:t2", window.ShownOutline!.Line.LineId);

            Assert.True(window.PressKey(Key.Up, KeyModifiers.Alt));
            Assert.Equal("line:t1", window.ShownOutline!.Line.LineId);
            Assert.False(window.PressKey(Key.Up, KeyModifiers.Alt));

            // 커맨드가 없는 t2를 건너뛴다.
            Assert.True(window.PressKey(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift));
            Assert.Equal("line:t3", window.ShownOutline!.Line.LineId);
            window.Close();
        });
    }

    [Fact]
    public void 에피소드를_접으면_머리만_남는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();
            string first = window.ListedEpisodes[0].Id;
            int all = window.VisibleLines.Count;

            window.ToggleEpisode(first);

            Assert.True(window.IsCollapsed(first));
            Assert.Equal(all - 3, window.VisibleLines.Count);
            Assert.Null(window.ShownOutline);
            RenderShots.Save(window, "k4-5-collapsed");

            window.ToggleEpisode(first);
            Assert.Equal(all, window.VisibleLines.Count);
            window.Close();
        });
    }

    [Fact]
    public void 타임라인은_넓히고_좁힌다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();

            window.SetTimelineZoom(3);
            Assert.Equal(3, window.TimelineZoom);
            RenderShots.Save(window, "k4-5-zoom");

            window.SetTimelineZoom(0.2);
            Assert.Equal(1, window.TimelineZoom);

            // 넓혀도 끌어 고치는 양은 같다(초로 셈한다).
            window.SetTimelineZoom(4);
            Assert.True(window.DragBar("t1/in/placement/0", 0.5f)!.Changed);
            window.Close();
        });
    }

    [Fact]
    public void 창의_꼴은_닫을_때_남고_다시_켜면_돌아온다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, string settings) = Open();
            window.SetSideWidths(300, 420);
            window.Close();

            Assert.NotNull(ToolSettings.Load(settings).Window);

            MainWindow again = new(settings);
            again.Show();
            Assert.Equal((300, 420), again.SideWidths);

            // 너무 좁은 값은 받지 않는다.
            again.SetSideWidths(10, 10);
            Assert.Equal((300, 420), again.SideWidths);
            again.Close();
        });
    }

    [Fact]
    public void 단축키의_표는_겹치지_않고_F1로_열린다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _, _) = Open();

            Assert.Equal(MainWindow.Shortcuts.Count, MainWindow.Shortcuts.Select(s => (s.Key, s.Modifiers)).Distinct().Count());

            Assert.True(window.PressKey(Key.F1));
            Avalonia.Controls.Window table = window.ShowShortcuts()!;
            RenderShots.Save(table, "k4-5-shortcuts");
            table.Close();
            window.Close();
        });
    }
}
