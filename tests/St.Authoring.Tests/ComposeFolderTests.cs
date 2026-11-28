using System.Text.Json;
using St.Authoring.Composing;
using St.Cli;

namespace St.Authoring.Tests;

/// <summary>폴더에서 돌리는 합치기와 그 명령줄(K8 I2) — 짝의 설정 · 지문 기록 · 손으로 고친 게임 yarn. 대본은 지어낸 것이다.</summary>
public class ComposeFolderTests
{
    private const string Scene = ComposeTests.Scene;

    /// <summary>이야기 폴더 하나 — <c>exported/yarn</c>(대사 층) · <c>staging</c>(연출 층)과 그 밖의 게임 폴더.</summary>
    private sealed class Story
    {
        public Story(string? staging = ComposeTests.Staging)
        {
            Root = Directory.CreateTempSubdirectory("st-compose-").FullName;
            Directory.CreateDirectory(Dialogue = Path.Combine(Root, "story", "exported", "yarn"));
            Directory.CreateDirectory(Staging = Path.Combine(Root, "story", "staging"));
            Directory.CreateDirectory(Game = Path.Combine(Root, "game"));
            File.WriteAllText(Path.Combine(Dialogue, Scene), ComposeTests.Dialogue);

            if (staging is not null)
            {
                File.WriteAllText(Path.Combine(Staging, Scene), staging);
            }
        }

        public string Root { get; }

        public string Dialogue { get; }

        public string Staging { get; }

        public string Game { get; }

        public string GameFile => Path.Combine(Game, Scene);

        public string StagingFile => Path.Combine(Staging, Scene);

        public string RetiredFile => Path.Combine(Staging, LayerComposer.RetiredNameOf(Scene));

        public void Write(string dialogue) => File.WriteAllText(Path.Combine(Dialogue, Scene), dialogue);

        /// <summary>처음 — 게임 폴더를 주고 합친다.</summary>
        public (int Code, string Out, string Err) First() => Run("compose", Staging, "--out", Game);

        public (int Code, string Out, string Err) Again(params string[] more) => Run(["compose", Staging, .. more]);
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int code = StCli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private const string Changed = "정말 잘 부탁드려요.";

    private static string Edited => ComposeTests.Dialogue.Replace("잘 부탁드립니다, 정말로요.", Changed);

    [Fact]
    public void 처음에는_게임_폴더를_주고_그_짝은_설정에_상대_경로로_남는다()
    {
        Story story = new();
        (int code, string output, string error) = story.First();

        Assert.True(code == 0, error);
        Assert.Contains("장면 1 · 줄 2", output);
        Assert.Contains("쓴 것 1", output);
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.GameFile));

        // 짝은 연출 층 폴더에서 본 상대 경로다 — 다른 컴퓨터에서도 선다.
        ComposeSettings? settings = ComposeSettings.Load(story.Staging);
        Assert.Equal(new ComposeSettings("../exported/yarn", "../../game"), settings);
        Assert.True(File.Exists(Path.Combine(story.Staging, ComposeFolder.RecordFileName)));

        // 다음부터는 폴더만 준다 — 바뀐 것이 없으면 아무것도 쓰지 않는다.
        DateTime written = File.GetLastWriteTimeUtc(story.GameFile);
        (code, output, _) = story.Again();
        Assert.Equal(0, code);
        Assert.Contains("쓴 것 0", output);
        Assert.Contains("그대로 2", output);
        Assert.Equal(written, File.GetLastWriteTimeUtc(story.GameFile));
    }

    [Fact]
    public void 설정이_없는데_게임_폴더를_주지_않으면_쓰는_법을_알린다()
    {
        Story story = new();
        (int code, _, string error) = story.Again();

        Assert.Equal(2, code);
        Assert.Contains("--out", error);
        Assert.False(File.Exists(story.GameFile));
    }

    [Fact]
    public void dry_run은_무엇이_바뀔지만_보이고_아무것도_쓰지_않는다()
    {
        Story story = new();
        (int code, string output, _) = Run("compose", story.Staging, "--out", story.Game, "--dry-run");

        Assert.Equal(0, code);
        Assert.Contains("dry-run", output);
        Assert.Contains("쓸 것 1", output);
        Assert.False(File.Exists(story.GameFile));
        Assert.Null(ComposeSettings.Load(story.Staging));
        Assert.False(File.Exists(Path.Combine(story.Staging, ComposeFolder.RecordFileName)));
    }

    [Fact]
    public void 작가가_고친_대사는_게임에_들어가고_연출_층에는_옛_글이_남는다()
    {
        Story story = new();
        Assert.Equal(0, story.First().Code);

        story.Write(Edited);
        (int code, string output, string error) = story.Again();

        Assert.True(code == 0, error);
        Assert.Contains("다시 볼 것 1", output);
        Assert.Contains("다시 볼 것  ln_a2", output);
        Assert.Contains("박자 다시 놓을 것", output);
        Assert.Contains("옛: 가람: 잘 부탁드립니다, 정말로요.", output);
        Assert.Contains("새: 가람: " + Changed, output);
        Assert.Contains($"가람: {Changed}[beat=a/] #line:ln_a2\n", File.ReadAllText(story.GameFile));
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.StagingFile));
    }

    [Fact]
    public void 지운_줄은_보관소_파일로_가고_되살리면_보관소가_걷힌다()
    {
        Story story = new();
        Assert.Equal(0, story.First().Code);

        story.Write(ComposeTests.Dialogue.Replace("가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n", string.Empty));
        (int code, string output, _) = story.Again();

        Assert.Equal(0, code);
        Assert.Contains("보관 1", output);
        Assert.Contains("[beat=a/]", File.ReadAllText(story.RetiredFile));
        Assert.DoesNotContain("ln_a2", File.ReadAllText(story.StagingFile));
        Assert.DoesNotContain("ln_a2", File.ReadAllText(story.GameFile));

        story.Write(ComposeTests.Dialogue);
        (code, output, _) = story.Again();

        Assert.Equal(0, code);
        Assert.Contains("복원 1", output);
        Assert.False(File.Exists(story.RetiredFile));
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.StagingFile));
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.GameFile));
    }

    [Fact]
    public void 손으로_고친_게임_yarn은_멈추고_알리며_force면_덮는다()
    {
        Story story = new();
        Assert.Equal(0, story.First().Code);

        string byHand = ComposeTests.Staging.Replace("face s1 05", "face s1 07");
        File.WriteAllText(story.GameFile, byHand);
        story.Write(Edited);

        (int code, string output, string error) = story.Again();

        Assert.Equal(1, code);
        Assert.Contains("멈춘 것 1", output);
        Assert.Contains("멈춤: " + Scene, error);
        Assert.Contains("누가 고쳤다", error);
        Assert.Equal(byHand, File.ReadAllText(story.GameFile));

        (code, output, _) = story.Again("--force");

        Assert.Equal(0, code);
        Assert.Contains("쓴 것 1", output);
        Assert.Contains(Changed, File.ReadAllText(story.GameFile));
        Assert.DoesNotContain("face s1 07", File.ReadAllText(story.GameFile));

        // 덮은 뒤로는 우리가 쓴 그대로다 — 다시 멈추지 않는다.
        Assert.Equal(0, story.Again().Code);
    }

    [Fact]
    public void 기록에_없는_게임_yarn은_같으면_받아들이고_다르면_멈춘다()
    {
        // 지금의 게임 yarn을 그대로 연출 층으로 옮긴 처음 — 바이트가 같으면 쓰지 않고 지문만 적는다.
        Story same = new();
        File.WriteAllText(same.GameFile, ComposeTests.Staging);
        (int code, string output, _) = same.First();

        Assert.Equal(0, code);
        Assert.Contains("쓴 것 0", output);
        Assert.True(File.Exists(Path.Combine(same.Staging, ComposeFolder.RecordFileName)));

        Story other = new();
        File.WriteAllText(other.GameFile, "title: X\n---\n===\n");
        (code, _, string error) = other.First();

        Assert.Equal(1, code);
        Assert.Contains("기록이 없는 파일", error);
        Assert.Equal("title: X\n---\n===\n", File.ReadAllText(other.GameFile));
    }

    [Fact]
    public void 대사_층이_비었으면_합치지_않는다()
    {
        Story story = new();
        File.Move(Path.Combine(story.Dialogue, Scene), Path.Combine(story.Root, "elsewhere.yarn"));

        (int code, _, string error) = story.First();

        Assert.Equal(1, code);
        Assert.Contains("대사 층에 yarn이 없다", error);
        Assert.Equal(ComposeTests.Staging, File.ReadAllText(story.StagingFile));
        Assert.False(File.Exists(story.RetiredFile));
    }

    [Fact]
    public void 연출_층이_없는_새_장면은_대사_층_그대로_서고_json으로도_읽는다()
    {
        Story story = new(staging: null);
        (int code, string output, string error) = Run("compose", story.Staging, "--out", story.Game, "--json");

        Assert.True(code == 0, error);
        Assert.Equal(ComposeTests.Dialogue, File.ReadAllText(story.GameFile));
        Assert.Equal(ComposeTests.Dialogue, File.ReadAllText(story.StagingFile));

        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement root = json.RootElement;
        Assert.Equal(1, root.GetProperty("scenes").GetInt32());
        Assert.Equal(2, root.GetProperty("lines").GetInt32());
        Assert.Equal(
            new string?[] { "noStaging", "noStaging" },
            root.GetProperty("notes").EnumerateArray().Select(n => n.GetProperty("kind").GetString()));
        Assert.Equal(
            new[] { "연출 층/written", "게임/written" },
            root.GetProperty("files").EnumerateArray().Select(f => $"{f.GetProperty("layer").GetString()}/{f.GetProperty("change").GetString()}"));
    }
}
