using St.Authoring.Composing;
using St.Authoring.Editing;

namespace St.App.Tests;

/// <summary>층의 할 일(K8 I4) — 딱지와 "할 일" 필터, 옛 글과 새 글 · 확인, 손으로 고친 게임 yarn의 덮어쓰기. 대본은 지어낸 것이다.</summary>
public class LayersTests
{
    private const string Scene = "ch01_s01.yarn";

    private const string Head = "// 장면: 작업실 (s01)\n\n// 에피소드: 첫 출근\ntitle: ch01_s01_ep01\n---\n";

    // 연출 층 — 연출가가 마지막으로 본 대사와 그 연출.
    private const string Staging = Head +
        "<<at in fade_in s1 0.57s>>\n<<gap in 0.4s>>\n" +
        "가람: [beat=in/]오늘부터 여기서 일하게 됐어요. #auto=2.0s #line:ln_a1\n" +
        "\n<<at start face s1 05>>\n<<at a face_swap s1 03 13fr>>\n" +
        "가람: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n" +
        "===\n";

    // 대사 층 — 작가가 둘째 줄을 고치고 그 앞에 한 줄을 넣었다.
    private const string Dialogue = Head +
        "가람: 오늘부터 여기서 일하게 됐어요. #line:ln_a1\n" +
        "가람: 저는 가람이라고 해요. #line:ln_a3\n" +
        "가람: 정말 잘 부탁드려요. #line:ln_a2\n" +
        "===\n";

    private sealed record Story(string Staging, string Game)
    {
        public string StagingFile => Path.Combine(Staging, Scene);

        public string GameFile => Path.Combine(Game, Scene);
    }

    private static MainWindow Open(out Story story, bool layered = true, string dialogueText = Dialogue)
    {
        string root = Directory.CreateTempSubdirectory("st-k8-i4-").FullName;
        string dialogue = Path.Combine(root, "story", "exported", "yarn");
        story = new Story(Path.Combine(root, "story", "staging"), Path.Combine(root, "game"));
        Directory.CreateDirectory(dialogue);
        Directory.CreateDirectory(story.Staging);
        Directory.CreateDirectory(story.Game);
        File.WriteAllText(Path.Combine(dialogue, Scene), dialogueText);
        File.WriteAllText(story.StagingFile, Staging);

        if (layered)
        {
            new ComposeSettings(ComposeSettings.DefaultDialogue, "../../game").Save(story.Staging);
        }

        MainWindow window = new(Path.Combine(Directory.CreateTempSubdirectory("st-k8-i4-set-").FullName, "settings.json"));
        window.Show();
        window.OpenPath(story.Staging);
        return window;
    }

    [Fact]
    public void 연출_층을_열면_할_일의_수가_서고_켠_할_일이_있는_줄만_남는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out _);

            // 열면서 합쳤다 — 새 줄이 연출 층에 들어와 목록에 선다.
            Assert.True(window.TodoShown);
            Assert.Equal(new string?[] { "ln_a1", "ln_a3", "ln_a2" }, window.VisibleLines.Select(l => l.Id));
            Assert.Equal(1, window.TodoCounts[MainWindow.Todo.Review]);
            Assert.Equal(1, window.TodoCounts[MainWindow.Todo.Beats]);
            Assert.Equal(1, window.TodoCounts[MainWindow.Todo.NoStaging]);
            Assert.Contains("대사 층과 합쳤다", window.StatusLine);
            RenderShots.Save(window, "k8-i4-todo");

            window.SetTodos(MainWindow.Todo.Review);
            Assert.Equal(new string?[] { "ln_a2" }, window.VisibleLines.Select(l => l.Id));

            window.SetTodos(MainWindow.Todo.NoStaging);
            Assert.Equal(new string?[] { "ln_a3" }, window.VisibleLines.Select(l => l.Id));

            // 둘을 켜면 어느 하나라도 있는 줄.
            window.SetTodos(MainWindow.Todo.Review, MainWindow.Todo.NoStaging);
            Assert.Equal(new string?[] { "ln_a3", "ln_a2" }, window.VisibleLines.Select(l => l.Id));

            window.SetTodos();
            Assert.Equal(3, window.VisibleLines.Count);
        });
    }

    [Fact]
    public void 층이_없는_폴더에는_할_일이_서지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out Story story, layered: false);

            Assert.False(window.TodoShown);
            Assert.False(window.OverwriteShown);
            Assert.Equal(2, window.VisibleLines.Count);
            Assert.True(window.SelectLine("ln_a2"));
            Assert.Null(window.ShownReview);
            Assert.False(window.ConfirmLine());
            Assert.False(File.Exists(story.GameFile));
        });
    }

    [Fact]
    public void 다시_볼_줄은_옛_글과_새_글이_나란히_서고_확인하면_연출_층의_대사가_새_글이_된다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out Story story);
            Assert.True(window.SelectLine("ln_a2"));

            LineCompare review = Assert.IsType<LineCompare>(window.ShownReview);
            Assert.Equal("가람: 잘 부탁드립니다, 정말로요.", review.OldText);
            Assert.Equal("가람: 정말 잘 부탁드려요.", review.NewText);
            Assert.True(review.BeatsMoved);
            RenderShots.Save(window, "k8-i4-review");

            // 확인 전 — 연출 층에는 옛 글, 게임에는 새 글.
            Assert.Contains("가람: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n", File.ReadAllText(story.StagingFile));
            Assert.Contains("가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_a2\n", File.ReadAllText(story.GameFile));

            Assert.True(window.ConfirmLine());
            Assert.Contains("박자는 줄 끝에 남았다", window.StatusLine);
            Assert.Contains("가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_a2\n", File.ReadAllText(story.StagingFile));
            Assert.Equal(File.ReadAllText(story.StagingFile), File.ReadAllText(story.GameFile));
            Assert.Null(window.ShownReview);
            Assert.Equal(0, window.TodoCounts[MainWindow.Todo.Review]);
            Assert.Equal(0, window.TodoCounts[MainWindow.Todo.Beats]);

            // 커맨드는 그대로다 — 박자 a에 걸린 것도.
            Assert.Contains("<<at a face_swap s1 03 13fr>>\n", File.ReadAllText(story.StagingFile));

            // 되돌리기 한 칸.
            Assert.True(window.Undo());
            Assert.Equal(1, window.TodoCounts[MainWindow.Todo.Review]);
            Assert.Contains("가람: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n", File.ReadAllText(story.StagingFile));
        });
    }

    [Fact]
    public void 손으로_고친_게임_yarn에는_덮어쓰기가_서고_누르면_다시_합쳐_쓴다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out Story story);
            Assert.False(window.OverwriteShown);

            string byHand = File.ReadAllText(story.GameFile).Replace("face s1 05", "face s1 09");
            File.WriteAllText(story.GameFile, byHand);

            // 연출 층을 고친다(확인) — 게임 yarn에는 쓰지 못한다.
            Assert.True(window.SelectLine("ln_a2"));
            Assert.True(window.ConfirmLine());
            Assert.True(window.OverwriteShown);
            Assert.Equal(byHand, File.ReadAllText(story.GameFile));
            RenderShots.Save(window, "k8-i4-held");

            Assert.True(window.OverwriteGame());
            Assert.False(window.OverwriteShown);
            Assert.Equal(File.ReadAllText(story.StagingFile), File.ReadAllText(story.GameFile));
            Assert.DoesNotContain("face s1 09", File.ReadAllText(story.GameFile));
        });
    }

    [Fact]
    public void 박자를_골라_글자를_누르면_그_앞에_놓이고_다시_볼_줄은_확인된다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = Open(out Story story);
            Assert.True(window.SelectLine("ln_a2"));

            // 다시 볼 줄 — 새 글 위에 박자가 줄 끝에 서 있다. 옮길 박자가 하나라 이미 골라져 있다.
            BeatLine beats = Assert.IsType<BeatLine>(window.ShownBeats);
            Assert.Equal("정말 잘 부탁드려요.", beats.Body);
            Assert.Equal("a", window.PickedBeat);
            RenderShots.Save(window, "k8-i5-beats");

            Assert.False(window.PlaceBeatAt(0));
            Assert.True(window.PlaceBeatAt(3));
            Assert.Contains("박자 a를 놓았다", window.StatusLine);
            Assert.Contains("확인했다", window.StatusLine);
            Assert.Contains("가람: 정말 [beat=a/]잘 부탁드려요. #line:ln_a2\n", File.ReadAllText(story.StagingFile));
            Assert.Equal(File.ReadAllText(story.StagingFile), File.ReadAllText(story.GameFile));
            Assert.Null(window.ShownReview);
            Assert.Equal(3, window.ShownBeats!.Beats.Single().Position);
            RenderShots.Save(window, "k8-i5-placed");

            // 확인된 줄에서도 다시 옮긴다 — 글은 그대로다.
            Assert.True(window.PlaceBeatAt(5));
            Assert.Contains("가람: 정말 잘 [beat=a/]부탁드려요. #line:ln_a2\n", File.ReadAllText(story.StagingFile));

            Assert.True(window.Undo());
            Assert.True(window.Undo());
            Assert.NotNull(window.ShownReview);

            // 옮길 박자가 없는 줄에는 칸이 서지 않는다 — 글 첫머리의 박자는 옮기지 않는다.
            Assert.True(window.SelectLine("ln_a1"));
            Assert.Null(window.ShownBeats);
            Assert.False(window.PickBeat("in"));
        });
    }

    [Fact]
    public void 보관소를_펴서_이_줄로_옮기면_연출이_붙고_보관소에서_빠진다()
    {
        HeadlessUi.Run(() =>
        {
            // 작가가 둘째 줄을 지우고 새 줄(ln_b1)로 다시 썼다.
            MainWindow window = Open(out Story story, dialogueText: Head +
                "가람: 오늘부터 여기서 일하게 됐어요. #line:ln_a1\n" +
                "가람: 정말 잘 부탁드려요. #line:ln_b1\n" +
                "===\n");

            Assert.True(window.SelectLine("ln_b1"));
            Assert.Equal("ln_a2", CommandAddress.ShortLineId(Assert.Single(window.ShelfLines).Line.LineId));
            Assert.Equal(1, window.TodoCounts[MainWindow.Todo.NoStaging]);

            window.SetShelfOpen(true);
            RenderShots.Save(window, "k8-i6-shelf");

            Assert.False(window.AdoptShelved("ln_zz"));
            Assert.True(window.AdoptShelved("ln_a2"));
            Assert.Contains("보관소의 연출을 이 줄에 붙였다", window.StatusLine);
            Assert.Contains(
                "<<at start face s1 05>>\n<<at a face_swap s1 03 13fr>>\n가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_b1\n",
                File.ReadAllText(story.StagingFile));
            Assert.Equal(File.ReadAllText(story.StagingFile), File.ReadAllText(story.GameFile));
            Assert.Empty(window.ShelfLines);
            Assert.Equal(0, window.TodoCounts[MainWindow.Todo.NoStaging]);

            // 줄 끝에 선 박자를 바로 놓는다.
            Assert.Equal("a", window.PickedBeat);
            Assert.True(window.PlaceBeatAt(3));
            RenderShots.Save(window, "k8-i6-adopted");

            Assert.True(window.Undo());
            Assert.True(window.Undo());
            Assert.Single(window.ShelfLines);
            Assert.DoesNotContain("face_swap", File.ReadAllText(story.StagingFile));
        });
    }
}
