using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.App.Tests;

/// <summary>고치는 화면(K4-2) — 칸 · 지연 · 간격 · 빼기 · 되돌리기. 조작마다 바로 파일에 쓴다.</summary>
public class EditingTests
{
    // 지어낸 대본 — 진행 파일이 없어 파일이 장면, 노드가 에피소드로 열린다.
    private const string Script =
        "title: T\n---\n" +
        "<<at in fade_out s1 15fr>>\n<<at in pause 15fr>>\n<<at in face s2 05>>\n<<gap in 1s>>\n" +
        "<<at start size s1 front 10fr>>\n<<at start motion s2 nod>>\n<<at a face_swap s2 03>> // card:E04\n" +
        "가: [beat=in/]하나 [beat=a/]둘 #line:t1\n\n" +
        "<<fade_in s1 10fr>>\n나: 옛 표기 #line:t2\n===\n";

    private static (MainWindow Window, string Path) Open()
    {
        string dir = Directory.CreateTempSubdirectory("st-edit-").FullName;
        string path = Path.Combine(dir, "a.yarn");
        File.WriteAllText(path, Script);

        MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-edit-set-").FullName, "settings.json"));
        window.Show();
        window.OpenPath(dir);
        window.SelectLine("t1");
        return (window, path);
    }

    [Fact]
    public void 커맨드를_고르면_지연과_칸이_서고_적으면_파일에_쓰인다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();

            Assert.True(window.SelectCommand("t1/a/acting/0"));
            Assert.Equal(new[] { "지연", "슬롯", "표정", "길이" }, window.InspectorFields);
            RenderShots.Save(window, "k4-2-command");

            Assert.True(window.EditField("표정", "07"));

            // 그 줄 하나만 바뀐다 — 줄 끝의 카드 출처는 그대로다.
            Assert.Equal(Script.Replace("face_swap s2 03", "face_swap s2 07"), File.ReadAllText(path));
            Assert.Equal("07", window.ShownOutline!.Steps[2].Commands[0].Args[1].Value);
            Assert.Equal("t1/a/acting/0", window.PickedCommand?.ToString());
            Assert.Null(window.EditMessage);
            window.Close();
        });
    }

    [Fact]
    public void 적지_않은_칸에_적고_비우면_다시_사라진다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectCommand("t1/a/acting/0");

            Assert.True(window.EditField("길이", "20fr"));
            Assert.Contains("<<at a face_swap s2 03 20fr>> // card:E04", File.ReadAllText(path));

            Assert.True(window.EditField("길이", string.Empty));
            Assert.Equal(Script, File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 고르는_칸과_낱말_칸을_고친다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();

            window.SelectCommand("t1/start/acting/0");
            Assert.True(window.EditField("읽는 법", "absolute"));
            Assert.Contains("<<at start motion s2 nod absolute>>", File.ReadAllText(path));

            window.SelectCommand("t1/start/placement/0");
            Assert.True(window.EditField("Y 그대로", "켬"));
            Assert.Contains("<<at start size s1 front keep_y 10fr>>", File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 지연을_적으면_뒤_커맨드가_늦어지고_고른_커맨드를_그대로_쥔다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectCommand("t1/a/acting/0");

            Assert.True(window.EditField("지연", "12fr"));

            Assert.Contains("<<at a pause 12fr>>\n<<at a face_swap s2 03>> // card:E04", File.ReadAllText(path));
            Assert.Equal(0.4f, window.ShownOutline!.Steps[2].Commands[0].Delay, 3);
            Assert.Equal("t1/a/acting/0", window.PickedCommand?.ToString());

            // 앞에 이미 기다림이 있는 커맨드 — 그 길이를 고친다. 비우면 0이다.
            window.SelectCommand("t1/in/acting/0");
            Assert.True(window.EditField("지연", "1s"));
            Assert.Contains("<<at in pause 1s>>", File.ReadAllText(path));
            Assert.True(window.EditField("지연", string.Empty));
            Assert.DoesNotContain("<<at in pause", File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 스텝을_고르면_간격을_고친다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();

            Assert.True(window.SelectStep("in"));
            Assert.Equal(new[] { "간격" }, window.InspectorFields);
            RenderShots.Save(window, "k4-2-step");

            Assert.True(window.EditField("간격", "2s"));
            Assert.Contains("<<gap in 2s>>", File.ReadAllText(path));
            Assert.Equal("in", window.PickedAnchor);

            window.SelectStep("end");
            Assert.True(window.EditField("간격", "15fr"));
            Assert.Contains("<<gap end 15fr>>\n가:", File.ReadAllText(path));

            Assert.True(window.EditField("간격", string.Empty));
            Assert.DoesNotContain("<<gap end", File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 커맨드를_빼면_고른_것을_놓는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectCommand("t1/start/acting/0");

            Assert.True(window.ApplyEdit(new RemoveCommand(window.PickedCommand!.Value))!.Changed);

            Assert.DoesNotContain("motion s2 nod", File.ReadAllText(path));
            Assert.Null(window.PickedCommand);
            window.Close();
        });
    }

    [Fact]
    public void 읽지_못할_값은_거절하고_까닭을_보인다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectCommand("t1/a/acting/0");

            Assert.True(window.EditField("길이", "빠르게"));

            Assert.Equal(Script, File.ReadAllText(path));
            Assert.Contains("시간이다", window.EditMessage);

            // 까닭은 화면의 말로 온다 — yarn의 표기(<<…>>)와 칸의 열쇠는 보이지 않는다.
            Assert.DoesNotContain("<<", window.EditMessage);
            Assert.DoesNotContain("(duration)", window.EditMessage);
            RenderShots.Save(window, "k4-2-refused");

            // 다음 고침이 받아들여지면 까닭은 사라진다.
            Assert.True(window.EditField("길이", "20fr"));
            Assert.Null(window.EditMessage);
            window.Close();
        });
    }

    [Fact]
    public void 되돌리기와_다시_하기가_파일에_쓰고_화면이_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectCommand("t1/a/acting/0");
            window.EditField("표정", "07");
            window.EditField("지연", "12fr");

            Assert.True(window.Undo());
            Assert.DoesNotContain("pause 12fr", File.ReadAllText(path));
            Assert.Equal("07", window.ShownOutline!.Steps[2].Commands[0].Args[1].Value);

            Assert.True(window.Undo());
            Assert.Equal(Script, File.ReadAllText(path));
            Assert.False(window.Undo());

            Assert.True(window.Redo());
            Assert.Contains("face_swap s2 07", File.ReadAllText(path));
            Assert.Equal("t1", window.ShownOutline!.Line.LineId[5..]);
            window.Close();
        });
    }

    [Fact]
    public void 읽기만인_줄은_칸이_잠기고_고쳐지지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectLine("t2");

            Assert.True(window.SelectCommand("t2/start/placement/0"));
            Assert.False(window.EditField("길이", "20fr"));
            Assert.False(window.ApplyEdit(new SetValue(window.PickedCommand!.Value, "duration", "20fr"))!.Ok);
            Assert.Equal(Script, File.ReadAllText(path));
            window.Close();
        });
    }

    [Fact]
    public void 바깥에서_바뀐_대본을_다시_읽는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, string path) = Open();
            window.SelectCommand("t1/a/acting/0");
            window.EditField("표정", "07");

            File.WriteAllText(path, Script.Replace("motion s2 nod", "motion s2 bow"));

            Assert.True(window.RefreshFromDisk());
            Assert.Equal("bow", window.ShownOutline!.Steps[1].Commands[1].Args[1].Value);
            Assert.Equal("03", window.ShownOutline.Steps[2].Commands[0].Args[1].Value);
            Assert.False(window.Undo());
            Assert.False(window.RefreshFromDisk());
            window.Close();
        });
    }

    [Fact]
    public void 종류로_가려진_커맨드는_고른_채로_남지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            (MainWindow window, _) = Open();
            window.SelectCommand("t1/a/acting/0");

            window.SetKinds(CommandKind.Camera);

            Assert.Null(window.PickedCommand);
            Assert.Empty(window.InspectorFields);
            window.Close();
        });
    }
}
