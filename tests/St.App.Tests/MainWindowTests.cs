using Ked.Presentation.Core;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.App.Tests;

/// <summary>읽는 화면(K4-1) — 대본 열기 · 줄 찾기 · 종류 필터 · 스텝 목록.</summary>
public class MainWindowTests
{
    // 지어낸 대본 — 대사 원문이 든 대본은 이 저장소에 넣지 않는다.
    private const string Script =
        "title: T\n---\n" +
        "<<at in fade_out s1 15fr>>\n<<at in pause 15fr>>\n<<at in face s2 05>>\n<<gap in 1s>>\n" +
        "<<at start shot_zoom 1.2 0.4s>>\n<<at a face_swap s2 03>>\n<<at a bgm theme>>\n<<at end emoji s2 star>>\n" +
        "가: [beat=in/]하나 [beat=a/]둘 [beat=b/]셋 #line:t1\n\n" +
        "<<fade_in s1 10fr>>\n나: 옛 표기 #line:t2\n\n" +
        "다: 커맨드가 없는 줄 #line:t3\n===\n";

    private static string Folder()
    {
        string dir = Directory.CreateTempSubdirectory("st-app-").FullName;
        File.WriteAllText(Path.Combine(dir, "a.yarn"), Script);
        return dir;
    }

    private static string Settings() => Path.Combine(Directory.CreateTempSubdirectory("st-app-set-").FullName, "settings.json");

    private static MainWindow Open(string folder)
    {
        MainWindow window = new(Settings());
        window.Show();
        window.OpenPath(folder);
        return window;
    }

    [Fact]
    public void 대본이_없으면_여는_법을_알린다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = new(Settings());
            window.Show();

            Assert.Empty(window.ListedLines);
            Assert.Null(window.ShownOutline);
            Assert.Contains("대본 폴더", window.StatusLine);
            RenderShots.Save(window, "k4-1-empty");
            window.Close();
        });
    }

    [Fact]
    public void 폴더를_열면_줄이_서고_다음에_켜면_다시_열린다()
    {
        HeadlessUi.Run(() =>
        {
            string folder = Folder();
            string settings = Settings();

            MainWindow window = new(settings);
            window.Show();
            window.OpenPath(folder);

            Assert.Equal(new[] { "t1", "t2", "t3" }, window.ListedLines.Select(l => l.Id));
            Assert.Equal(folder, ToolSettings.Load(settings).LastPath);
            window.Close();

            MainWindow again = new(settings);
            again.Show();
            Assert.Equal(3, again.ListedLines.Count);
            again.Close();
        });
    }

    [Fact]
    public void 찾기와_종류로_목록을_좁힌다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(Folder());

            window.SearchFor("옛");
            Assert.Equal(new[] { "t2" }, window.ListedLines.Select(l => l.Id));

            window.SearchFor(string.Empty);
            window.SetOnlyWithKind(true);
            Assert.Equal(new[] { "t1", "t2" }, window.ListedLines.Select(l => l.Id));

            window.SetKinds(CommandKind.Camera);
            Assert.Equal(new[] { "t1" }, window.ListedLines.Select(l => l.Id));
            window.Close();
        });
    }

    [Fact]
    public void 줄을_고르면_스텝과_고른_종류의_커맨드가_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(Folder());

            Assert.True(window.SelectLine("t1"));
            LineOutline all = window.ShownOutline!;
            Assert.Equal(new[] { "in", "start", "a", "b", "end" }, all.Steps.Select(s => s.Anchor));
            Assert.Equal(5, all.ShownCommands);
            Assert.Equal(1, all.Steps[2].Others[CommandKind.Sound]);
            RenderShots.Save(window, "k4-1-line");

            window.SetKinds(CommandKind.Acting);
            LineOutline acting = window.ShownOutline!;
            Assert.Equal(new[] { "face", "face_swap", "emoji" }, acting.Steps.SelectMany(s => s.Commands).Select(c => c.Name));
            Assert.Equal(0.5f, acting.Steps[0].Commands[0].Delay, 3);
            RenderShots.Save(window, "k4-1-acting");
            window.Close();
        });
    }

    [Fact]
    public void 읽기만인_줄은_까닭이_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(Folder());

            Assert.True(window.SelectLine("t2"));
            Assert.False(window.ShownOutline!.Editable);
            Assert.Contains("옛 표기", window.ShownOutline.ReadOnlyReason);
            RenderShots.Save(window, "k4-1-readonly");
            window.Close();
        });
    }

    [Fact]
    public void 목록에서_빠진_줄은_고른_채로_남지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(Folder());
            window.SelectLine("t1");

            window.SearchFor("옛");

            Assert.Null(window.ShownOutline);
            Assert.False(window.SelectLine("t1"));
            window.Close();
        });
    }
}
