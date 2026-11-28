using Ked.Presentation.Core;
using St.Authoring.Composing;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.Authoring.Tests;

/// <summary>연출 층 폴더를 연 자리 — 열 때 · 고칠 때 · 다시 볼 때 합친다(K8 I3). 대본은 지어낸 것이다.</summary>
public class LayeredSessionTests
{
    private const string Scene = ComposeTests.Scene;

    private static readonly CommandAddress Face = new("line:ln_a2", "start", CommandKind.Expression, 0);

    private sealed record Story(string Dialogue, string Staging, string Game)
    {
        public string DialogueFile => Path.Combine(Dialogue, Scene);

        public string StagingFile => Path.Combine(Staging, Scene);

        public string GameFile => Path.Combine(Game, Scene);

        public string RetiredFile => Path.Combine(Staging, LayerComposer.RetiredNameOf(Scene));
    }

    // 이야기 폴더 — exported/(진행 파일) · exported/yarn(대사 층) · staging(연출 층 + 짝의 설정), 그 밖의 게임 폴더.
    private static Story Make()
    {
        string root = Directory.CreateTempSubdirectory("st-layers-").FullName;
        Story story = new(Path.Combine(root, "story", "exported", "yarn"), Path.Combine(root, "story", "staging"), Path.Combine(root, "game"));
        Directory.CreateDirectory(story.Dialogue);
        Directory.CreateDirectory(story.Staging);
        Directory.CreateDirectory(story.Game);

        File.WriteAllText(Path.Combine(root, "story", "exported", "ch01.progression.json"), """
            {
              "ChapterId": "ch01", "DisplayName": "1편 · 층", "StartEpisodeId": "ch01_s01_ep01", "Stats": [],
              "Nodes": [ { "EpisodeId": "ch01_s01_ep01", "Title": "첫 출근", "DialogueEntryId": "ch01_s01_ep01", "SceneId": "s01" } ]
            }
            """);

        File.WriteAllText(story.DialogueFile, ComposeTests.Dialogue);
        File.WriteAllText(story.StagingFile, ComposeTests.Staging);
        new ComposeSettings(ComposeSettings.DefaultDialogue, "../../game").Save(story.Staging);
        return story;
    }

    private static IEnumerable<LibraryLine> Lines(EditSession session)
        => session.Library.Chapters.SelectMany(c => c.Scenes).SelectMany(s => s.Episodes).SelectMany(e => e.Lines);

    [Fact]
    public void 연출_층을_열면_합치고_진행_파일은_짝지은_exported에서_읽는다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);

        Assert.NotNull(session.Layers);
        Assert.Null(session.ComposeProblem);
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.GameFile));

        StoryChapter chapter = Assert.Single(session.Library.Chapters);
        Assert.Equal("1편 · 층", chapter.Name);
        Assert.Equal("작업실", Assert.Single(chapter.Scenes).Name);
        Assert.Equal(new string?[] { "ln_a1", "ln_a2" }, Lines(session).Select(l => l.Id));
    }

    [Fact]
    public void 층이_없는_폴더는_합치지_않는다()
    {
        Story story = Make();
        File.Delete(Path.Combine(story.Staging, ComposeSettings.FileName));

        EditSession session = EditSession.Open(story.Staging);

        Assert.Null(session.Layers);
        Assert.Null(session.Composed);
        Assert.False(File.Exists(story.GameFile));
    }

    [Fact]
    public void 연출을_고치면_게임_yarn이_따라오고_되돌리면_돌아온다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);

        EditOutcome outcome = session.Apply(story.StagingFile, new SetValue(Face, "face", "07"));

        Assert.True(outcome.Changed, outcome.Error);
        Assert.Contains("<<at start face s1 07>>", File.ReadAllText(story.StagingFile));
        Assert.Equal(File.ReadAllText(story.StagingFile), File.ReadAllText(story.GameFile));
        Assert.Null(session.ComposeProblem);

        Assert.True(session.Undo());
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.StagingFile));
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.GameFile));

        Assert.True(session.Redo());
        Assert.Contains("<<at start face s1 07>>", File.ReadAllText(story.GameFile));
    }

    [Fact]
    public void 대사_층이_바뀌면_다시_볼_때_들인다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);
        Assert.False(session.Refresh());

        // 작가가 글을 고쳤다 — 게임에는 새 글이, 연출 층에는 옛 글이.
        File.WriteAllText(story.DialogueFile, ComposeTests.Dialogue.Replace("잘 부탁드립니다, 정말로요.", "정말 잘 부탁드려요."));
        int revision = session.Revision;

        Assert.True(session.Refresh());
        Assert.True(session.Revision > revision);
        Assert.Contains("가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_a2\n", File.ReadAllText(story.GameFile));
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.StagingFile));

        LineNote note = Assert.Single(session.Composed!.Result.Notes);
        Assert.Equal(LineNoteKind.Review, note.Kind);
        Assert.True(note.BeatsMoved);

        // 다시 볼 것이 남은 줄의 연출도 고칠 수 있다 — 게임 yarn에는 새 글과 고친 연출이 함께 간다.
        Assert.True(session.Apply(story.StagingFile, new SetValue(Face, "face", "07")).Changed);
        string game = File.ReadAllText(story.GameFile);
        Assert.Contains("<<at start face s1 07>>", game);
        Assert.Contains("가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_a2\n", game);
        Assert.Contains("가람: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n", File.ReadAllText(story.StagingFile));
    }

    [Fact]
    public void 새_줄은_연출_층에_들어와_바로_고칠_수_있고_지운_줄은_보관소로_가_목록에서_빠진다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);

        File.WriteAllText(story.DialogueFile, ComposeTests.Dialogue.Replace(
            "가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n", "가람: 저는 가람이라고 해요. #line:ln_a3\n"));

        Assert.True(session.Refresh());
        Assert.Equal(new string?[] { "ln_a1", "ln_a3" }, Lines(session).Select(l => l.Id));
        Assert.True(File.Exists(story.RetiredFile));

        // 보관소는 대본이 아니다 — 읽는 파일은 장면 하나뿐이다.
        Assert.Single(session.Library.Files);

        EditOutcome added = session.Apply(story.StagingFile, new AddCommand("line:ln_a3", "start", "face", ["s1", "03"]));
        Assert.True(added.Changed, added.Error);
        Assert.Contains("<<at start face s1 03>>\n가람: 저는 가람이라고 해요. #line:ln_a3\n", File.ReadAllText(story.GameFile));
    }

    [Fact]
    public void 손으로_고친_게임_yarn은_덮지_않고_까닭을_든다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);

        string byHand = ComposeTests.Staging.Replace("emoji s1 star", "emoji s1 heart");
        File.WriteAllText(story.GameFile, byHand);

        // 연출 층은 고쳐진다 — 정본이다. 게임 yarn만 멈춘다.
        Assert.True(session.Apply(story.StagingFile, new SetValue(Face, "face", "07")).Changed);
        Assert.Contains("<<at start face s1 07>>", File.ReadAllText(story.StagingFile));
        Assert.Equal(byHand, File.ReadAllText(story.GameFile));
        Assert.Contains("쓰지 않았다", session.ComposeProblem);
    }

    [Fact]
    public void 짝의_폴더가_없어도_연출_층은_열리고_까닭을_든다()
    {
        Story story = Make();
        Directory.Delete(story.Game);

        EditSession session = EditSession.Open(story.Staging);

        Assert.Contains("게임 폴더가 없다", session.ComposeProblem);
        Assert.Equal(2, Lines(session).Count());
    }

    [Fact]
    public void 다시_볼_줄에_박자를_놓으면_새_글_위에_놓이고_그_줄은_확인된다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);
        File.WriteAllText(story.DialogueFile, ComposeTests.Dialogue.Replace("잘 부탁드립니다, 정말로요.", "정말 잘 부탁드려요."));
        Assert.True(session.Refresh());

        LibraryLine line = Lines(session).Single(l => l.Id == "ln_a2");
        Assert.True(session.StateOf(line)!.BeatsMoved);

        // 보이는 것은 새 글이고 박자는 줄 끝에 가 있다.
        BeatLine beats = session.BeatsOf(line)!;
        Assert.Equal("정말 잘 부탁드려요.", beats.Body);
        Assert.Equal(new BeatSpot(0, "a", 11, Movable: true), Assert.Single(beats.Beats));

        EditOutcome outcome = session.PlaceBeat(line, 0, 3);
        Assert.True(outcome.Changed, outcome.Error);

        string placed = "가람: 정말 [beat=a/]잘 부탁드려요. #line:ln_a2\n";
        Assert.Contains(placed, File.ReadAllText(story.StagingFile));
        Assert.Contains(placed, File.ReadAllText(story.GameFile));

        // 놓으면 확인된 것이다 — 다시 볼 것이 없고, 박자 a의 커맨드는 그대로 붙어 있다.
        line = Lines(session).Single(l => l.Id == "ln_a2");
        Assert.Null(session.StateOf(line)!.Review);
        Assert.Equal("face_swap", line.Read.Step("a").Commands.Single().Name);
        Assert.Equal("잘 부탁드려요.", line.Read.Step("a").Text);

        // 되돌리기 한 칸 — 옛 글과 다시 볼 것으로 돌아온다.
        Assert.True(session.Undo());
        Assert.Contains("가람: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n", File.ReadAllText(story.StagingFile));
        Assert.NotNull(session.StateOf(Lines(session).Single(l => l.Id == "ln_a2"))!.Review);

        // 놓을 수 없는 자리는 거절하고 쓰지 않는다.
        Assert.False(session.PlaceBeat(Lines(session).Single(l => l.Id == "ln_a2"), 0, 0).Ok);
    }

    [Fact]
    public void 보관소의_연출을_새_줄로_옮기면_두_파일이_한_칸으로_되돌려진다()
    {
        Story story = Make();
        EditSession session = EditSession.Open(story.Staging);

        // 작가가 줄을 지우고 새 줄로 다시 썼다 — 줄 ID가 달라 연출이 저절로 돌아오지 않는다.
        File.WriteAllText(story.DialogueFile, ComposeTests.Dialogue.Replace(
            "가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n", "가람: 정말 잘 부탁드려요. #line:ln_b1\n"));
        Assert.True(session.Refresh());

        EditSession.ShelfEntry entry = Assert.Single(session.Shelved());
        Assert.Equal("line:ln_a2", entry.Line.LineId);
        Assert.Equal(story.RetiredFile, entry.Path);
        string shelf = File.ReadAllText(story.RetiredFile);

        EditOutcome outcome = session.Adopt(Lines(session).Single(l => l.Id == "ln_b1"), entry);
        Assert.True(outcome.Changed, outcome.Error);

        string moved = "<<at start face s1 05>>\n<<at a face_swap s1 03 13fr>>\n<<at end emoji s1 star>>\n가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_b1\n";
        Assert.Contains(moved, File.ReadAllText(story.StagingFile));
        Assert.Contains(moved, File.ReadAllText(story.GameFile));
        Assert.False(File.Exists(story.RetiredFile));
        Assert.Empty(session.Shelved());
        Assert.Null(session.ComposeProblem);

        // 줄 끝에 선 박자는 놓을 수 있다.
        LibraryLine line = Lines(session).Single(l => l.Id == "ln_b1");
        Assert.Equal("face_swap", line.Read.Step("a").Commands.Single().Name);
        Assert.True(session.BeatsOf(line)!.Beats.Single().Movable);

        // 되돌리기 한 칸 — 연출 층과 보관소가 함께 돌아온다.
        Assert.True(session.Undo());
        Assert.DoesNotContain("face_swap", File.ReadAllText(story.StagingFile));
        Assert.Equal(shelf, File.ReadAllText(story.RetiredFile));
        Assert.Single(session.Shelved());

        Assert.True(session.Redo());
        Assert.Contains(moved, File.ReadAllText(story.GameFile));
        Assert.False(File.Exists(story.RetiredFile));
    }
}
