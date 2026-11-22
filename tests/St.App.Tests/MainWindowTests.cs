using Ked.Presentation.Core;
using St.Authoring.Reading;
using St.Authoring.Settings;

namespace St.App.Tests;

/// <summary>읽는 화면(K4-1) — 대본 열기 · 줄 찾기 · 종류 필터 · 스텝 목록.</summary>
public class MainWindowTests
{
    // 지어낸 대본 — 대사 원문이 든 대본은 이 저장소에 넣지 않는다.
    // ked-story-director가 내보내는 꼴이다: 진행 파일 하나(챕터), 장면마다 yarn 파일 하나, 에피소드마다 노드 하나.
    private const string Stage =
        "// 장면: 무대 (stage)\n\n// 에피소드: 막이 오른다\ntitle: ch01_stage_01\n---\n" +
        "<<at in fade_out s1 15fr>>\n<<at in pause 15fr>>\n<<at in face s2 05>>\n<<gap in 1s>>\n" +
        "<<at start shot_zoom 1.2 0.4s>>\n<<at a face_swap s2 03>>\n<<at a bgm theme>>\n<<at end emoji s2 star>>\n" +
        "가: [beat=in/]하나 [beat=a/]둘 [beat=b/]셋 #line:t1\n\n" +
        "<<at start fade_in s1 10fr>>\n<<fade_out s2 10fr>>\n나: 실행되지 않는 명령 #line:t2\n===\n\n" +
        "// 에피소드: 갈래\ntitle: ch01_stage_01a\n---\n다: 커맨드가 없는 줄 #line:t3\n===\n";

    private const string Lobby =
        "// 장면: 로비 (lobby)\n\ntitle: ch01_lobby_01\n---\n<<at start face s1 3>>\n라: 다른 장면의 줄 #line:u1\n===\n";

    private const string Progression = """
        {
          "ChapterId": "ch01", "DisplayName": "1편 · 시험",
          "Nodes": [
            { "EpisodeId": "ch01_stage_01", "Title": "막이 오른다", "DialogueEntryId": "ch01_stage_01", "SceneId": "stage" },
            { "EpisodeId": "ch01_stage_01a", "Title": "갈래", "DialogueEntryId": "ch01_stage_01a", "SceneId": "stage" },
            { "EpisodeId": "ch01_lobby_01", "Title": "로비에서", "DialogueEntryId": "ch01_lobby_01", "SceneId": "lobby" }
          ]
        }
        """;

    private static string Folder()
    {
        string dir = Directory.CreateTempSubdirectory("st-app-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "yarn"));
        File.WriteAllText(Path.Combine(dir, "ch01.progression.json"), Progression);
        File.WriteAllText(Path.Combine(dir, "yarn", "ch01_stage.yarn"), Stage);
        File.WriteAllText(Path.Combine(dir, "yarn", "ch01_lobby.yarn"), Lobby);
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
    public void 폴더를_열면_첫_챕터의_첫_장면이_서고_다음에_켜면_다시_열린다()
    {
        HeadlessUi.Run(() =>
        {
            string folder = Folder();
            string settings = Settings();

            MainWindow window = new(settings);
            window.Show();
            window.OpenPath(folder);

            Assert.Equal("ch01", window.Chapter!.Id);
            Assert.Equal("무대", window.Scene!.Name);
            Assert.Equal(new[] { "막이 오른다", "갈래" }, window.ListedEpisodes.Select(e => e.Title));
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
    public void 장면을_바꾸면_그_장면의_에피소드가_서고_에피소드_하나만_볼_수_있다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(Folder());

            Assert.True(window.SelectEpisode("ch01_stage_01a"));
            Assert.Equal(new[] { "t3" }, window.ListedLines.Select(l => l.Id));
            RenderShots.Save(window, "k4-1-episode");

            Assert.True(window.SelectScene("lobby"));
            Assert.Null(window.Episode);
            Assert.Equal(new[] { "u1" }, window.ListedLines.Select(l => l.Id));

            Assert.False(window.SelectScene("없는장면"));
            window.Close();
        });
    }

    [Fact]
    public void 찾기와_종류로_목록을_좁힌다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(Folder());

            window.SearchFor("실행되지");
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

            window.SetKinds(CommandKind.Expression, CommandKind.Emoji);
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
            Assert.Contains("at 없는 명령", window.ShownOutline.ReadOnlyReason);
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

            window.SearchFor("실행되지");

            Assert.Null(window.ShownOutline);
            Assert.False(window.SelectLine("t1"));
            window.Close();
        });
    }
}
