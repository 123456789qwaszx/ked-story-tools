using Ked.Presentation.Core;
using St.Authoring.Composing;

namespace St.Authoring.Tests;

/// <summary>대사 층과 연출 층을 줄 ID로 합친다(K8 I1 — docs/PLAN.md §2.5). 대본은 지어낸 것이다.</summary>
public class ComposeTests
{
    internal const string Scene = "ch01_s01.yarn";

    // 대사 층 — 디렉터가 내는 꼴.
    internal const string Dialogue =
        "// 장면: 작업실 (s01)\n" +
        "\n" +
        "// 에피소드: 첫 출근\n" +
        "title: ch01_s01_ep01\n" +
        "---\n" +
        "가람: 오늘부터 여기서 일하게 됐어요. #line:ln_a1\n" +
        "가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n" +
        "===\n";

    private const string LeadA1 =
        "// 장면 머리 — 작업실, 한 사람\n" +
        "<<at in fade_in s1 0.57s>> // card:E01\n" +
        "<<gap in 0.4s>>\n";

    private const string LineA1 = "가람: [beat=in/]오늘부터 여기서 일하게 됐어요. #auto=2.0s #line:ln_a1\n";

    private const string LeadA2 =
        "\n" +
        "<<at start face s1 05>>\n" +
        "<<at a face_swap s1 03 13fr>>\n" +
        "<<at end emoji s1 star>>\n";

    private const string LineA2 = "가람: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n";

    private const string Head =
        "// 장면: 작업실 (s01)\n" +
        "\n" +
        "// 에피소드: 첫 출근\n" +
        "title: ch01_s01_ep01\n" +
        "---\n";

    // 연출 층 — 같은 뼈대에 줄마다 묶음 · 박자 표시 · 태그, 노드 끝에 빈 줄.
    internal const string Staging = Head + LeadA1 + LineA1 + LeadA2 + LineA2 + "\n===\n";

    private static Dictionary<string, string> One(string text, string name = Scene) => new() { [name] = text };

    private static ComposeResult Compose(string dialogue, string staging, IReadOnlyDictionary<string, string>? retired = null)
    {
        ComposeResult result = LayerComposer.Compose(One(dialogue), One(staging), retired);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result;
    }

    // 결과의 글 묶음에서 없어질 파일을 뺀 것 — 다음 합치기에 그대로 넣는다.
    private static Dictionary<string, string> Kept(IReadOnlyDictionary<string, string?> files)
        => files.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value!);

    // 새 연출 층과 보관소로 다시 합쳐도 같은 것이 나온다.
    private static void AssertSettled(IReadOnlyDictionary<string, string> dialogue, ComposeResult result)
    {
        ComposeResult again = LayerComposer.Compose(dialogue, Kept(result.Staging), Kept(result.Retired));
        Assert.True(again.Ok, string.Join("\n", again.Errors));
        Assert.Equal(Kept(result.Game), Kept(again.Game));
        Assert.Equal(Kept(result.Staging), Kept(again.Staging));
        Assert.Equal(Kept(result.Retired), Kept(again.Retired));
    }

    [Fact]
    public void 대사가_같으면_게임_yarn과_새_연출_층은_연출_층과_바이트까지_같다()
    {
        ComposeResult result = Compose(Dialogue, Staging);

        Assert.Equal(Staging, result.Game[Scene]);
        Assert.Equal(Staging, result.Staging[Scene]);
        Assert.Empty(result.Notes);
        Assert.Empty(result.Retired);
        AssertSettled(One(Dialogue), result);
    }

    [Fact]
    public void 글이_바뀐_줄은_새_글에_박자와_태그를_다시_얹고_연출_층에는_옛_글이_남는다()
    {
        string dialogue = Dialogue
            .Replace("오늘부터 여기서 일하게 됐어요.", "오늘부터 일해요.")
            .Replace("잘 부탁드립니다, 정말로요.", "정말 잘 부탁드려요.");

        ComposeResult result = Compose(dialogue, Staging);

        // 글 첫머리의 박자는 그 자리, 말 도중의 박자는 줄 끝(L5), 태그는 연출 층의 것 그대로.
        Assert.Equal(
            Head + LeadA1 + "가람: [beat=in/]오늘부터 일해요. #auto=2.0s #line:ln_a1\n"
            + LeadA2 + "가람: 정말 잘 부탁드려요.[beat=a/] #line:ln_a2\n" + "\n===\n",
            result.Game[Scene]);

        // 연출 층은 그대로다 — 옛 글이 기준이다.
        Assert.Equal(Staging, result.Staging[Scene]);

        Assert.Equal(
            new[]
            {
                new LineNote(LineNoteKind.Review, "line:ln_a1", Scene, "ch01_s01_ep01", "가람: 오늘부터 여기서 일하게 됐어요.", "가람: 오늘부터 일해요."),
                new LineNote(LineNoteKind.Review, "line:ln_a2", Scene, "ch01_s01_ep01", "가람: 잘 부탁드립니다, 정말로요.", "가람: 정말 잘 부탁드려요.", BeatsMoved: true),
            },
            result.Notes);

        // 합친 대본은 줄 읽기가 문제없이 읽는다 — 줄 끝으로 간 박자에도 커맨드가 붙어 있다.
        YarnFileRead read = YarnLineReader.ReadFile(result.Game[Scene]!, Scene, StageCommandSpecTable.Default);
        Assert.Empty(read.Problems);
        Assert.All(read.Lines, line => Assert.Empty(line.Problems));
        Assert.True(read.TryGet("ln_a2", out LineRead moved));
        Assert.Equal("face_swap", moved.Step("a").Commands.Single().Name);

        AssertSettled(One(dialogue), result);
    }

    [Fact]
    public void 화자만_바뀐_줄은_글과_박자의_자리가_그대로다()
    {
        string dialogue = Dialogue.Replace("가람: 잘 부탁드립니다", "나래: 잘 부탁드립니다");
        ComposeResult result = Compose(dialogue, Staging);

        Assert.Equal(
            Head + LeadA1 + LineA1 + LeadA2 + "나래: 잘 부탁드립니다, [beat=a/]정말로요. #line:ln_a2\n" + "\n===\n",
            result.Game[Scene]);
        Assert.Equal(Staging, result.Staging[Scene]);

        LineNote note = Assert.Single(result.Notes);
        Assert.Equal(LineNoteKind.Review, note.Kind);
        Assert.False(note.BeatsMoved);
    }

    [Fact]
    public void 새_줄은_연출_없이_그_자리에_끼운다()
    {
        string added = "가람: 저는 가람이라고 해요. #line:ln_a3\n";
        string dialogue = Dialogue.Replace("가람: 잘 부탁드립니다", added + "가람: 잘 부탁드립니다");
        ComposeResult result = Compose(dialogue, Staging);

        string expected = Head + LeadA1 + LineA1 + added + LeadA2 + LineA2 + "\n===\n";
        Assert.Equal(expected, result.Game[Scene]);
        Assert.Equal(expected, result.Staging[Scene]);
        Assert.Equal(new LineNote(LineNoteKind.NoStaging, "line:ln_a3", Scene, "ch01_s01_ep01"), Assert.Single(result.Notes));

        // 다음에는 연출 층에 그 줄이 있다 — 더는 새 줄이 아니다.
        ComposeResult again = Compose(dialogue, result.Staging[Scene]!);
        Assert.Empty(again.Notes);
    }

    [Fact]
    public void 지운_줄의_연출은_보관소로_가고_되살아나면_돌아온다()
    {
        string dialogue = Dialogue.Replace("가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n", string.Empty);
        ComposeResult result = Compose(dialogue, Staging);

        // 노드의 꼬리(=== 앞의 빈 줄)는 노드에 남는다.
        string expected = Head + LeadA1 + LineA1 + "\n===\n";
        Assert.Equal(expected, result.Game[Scene]);
        Assert.Equal(expected, result.Staging[Scene]);

        string shelf = LayerComposer.RetiredNameOf(Scene);
        Assert.Equal("ch01_s01.retired.yarn", shelf);
        Assert.True(LayerComposer.IsRetiredName(shelf));
        Assert.False(LayerComposer.IsRetiredName(Scene));
        Assert.Equal("title: ch01_s01_ep01\n---\n" + LeadA2 + LineA2 + "===\n", result.Retired[shelf]);
        Assert.Equal(new LineNote(LineNoteKind.Retired, "line:ln_a2", Scene, "ch01_s01_ep01"), Assert.Single(result.Notes));
        AssertSettled(One(dialogue), result);

        // 작가가 줄을 되살렸다 — 줄 ID가 같으면 보관한 연출이 돌아오고 보관소는 빈다(C3).
        ComposeResult back = Compose(Dialogue, result.Staging[Scene]!, Kept(result.Retired));
        Assert.Equal(Staging, back.Game[Scene]);
        Assert.Equal(Staging, back.Staging[Scene]);
        Assert.Null(back.Retired[shelf]);
        Assert.Equal(new LineNote(LineNoteKind.Restored, "line:ln_a2", Scene, "ch01_s01_ep01"), Assert.Single(back.Notes));
    }

    [Fact]
    public void 다른_노드나_다른_장면으로_간_줄은_연출이_따라간다()
    {
        string first = Dialogue.Replace("가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n", string.Empty);
        string second = "// 장면: 복도 (s02)\n\ntitle: ch01_s02_ep01\n---\n가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n===\n";

        ComposeResult result = LayerComposer.Compose(
            new Dictionary<string, string> { [Scene] = first, ["ch01_s02.yarn"] = second },
            One(Staging));

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(Head + LeadA1 + LineA1 + "\n===\n", result.Game[Scene]);
        Assert.Equal("// 장면: 복도 (s02)\n\ntitle: ch01_s02_ep01\n---\n" + LeadA2 + LineA2 + "===\n", result.Game["ch01_s02.yarn"]);
        Assert.Equal(result.Game["ch01_s02.yarn"], result.Staging["ch01_s02.yarn"]);
        Assert.Equal(new LineNote(LineNoteKind.Moved, "line:ln_a2", "ch01_s02.yarn", "ch01_s02_ep01"), Assert.Single(result.Notes));
        Assert.Empty(result.Retired);
    }

    [Fact]
    public void 갈래_안으로_간_줄은_대사_층의_들여쓰기를_따르고_다시_합쳐도_같다()
    {
        string dialogue = Dialogue.Replace(
            "가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n",
            "<<if $polite>>\n    가람: 잘 부탁드립니다, 정말로요. #line:ln_a2\n<<endif>>\n");

        ComposeResult result = Compose(dialogue, Staging);

        string expected = Head + LeadA1 + LineA1 + "<<if $polite>>\n" + LeadA2 + "    " + LineA2 + "<<endif>>\n" + "\n===\n";
        Assert.Equal(expected, result.Game[Scene]);
        Assert.Equal(expected, result.Staging[Scene]);
        Assert.Empty(result.Notes);
        AssertSettled(One(dialogue), result);
    }

    [Fact]
    public void 줄_ID가_한_층의_두_곳에_있으면_합치지_않는다()
    {
        string twice = Staging.Replace("\n===\n", "가람: 한 번 더. #line:ln_a1\n===\n");
        ComposeResult result = LayerComposer.Compose(One(Dialogue), One(twice));

        Assert.False(result.Ok);
        Assert.Contains("연출 층에 줄 ID #line:ln_a1가 두 번 있다", Assert.Single(result.Errors));
        Assert.Empty(result.Game);
        Assert.Empty(result.Staging);
    }

    [Fact]
    public void 새_장면은_대사_층_그대로_서고_사라진_장면은_걷히며_연출은_보관소에_남는다()
    {
        string fresh = "// 장면: 옥상 (s03)\n\ntitle: ch01_s03_ep01\n---\n가람: 바람이 좋네요. #line:ln_c1\n===\n";
        ComposeResult result = LayerComposer.Compose(One(fresh, "ch01_s03.yarn"), One(Staging));

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(fresh, result.Game["ch01_s03.yarn"]);
        Assert.Equal(fresh, result.Staging["ch01_s03.yarn"]);
        Assert.Null(result.Game[Scene]);
        Assert.Null(result.Staging[Scene]);
        Assert.Equal(
            "title: ch01_s01_ep01\n---\n" + LeadA1 + LineA1 + LeadA2 + LineA2 + "===\n",
            result.Retired["ch01_s01.retired.yarn"]);
        Assert.Equal(
            new[] { LineNoteKind.NoStaging, LineNoteKind.Retired, LineNoteKind.Retired },
            result.Notes.Select(n => n.Kind));
    }

    [Fact]
    public void 합친_대본에_없던_문제가_생기면_아무것도_내지_않는다()
    {
        // 보관소의 줄이 명세 표에 없는 명령을 달고 돌아오면 받은 대본에 없던 문제다 — 보관소는 사람이 손댈 수 있는 파일이다.
        string dialogue = Dialogue.Replace("===\n", "가람: 돌아온 줄. #line:ln_z9\n===\n");
        Dictionary<string, string> shelf = One("title: ch01_s01_ep01\n---\n<<at start no_such_command s1>>\n가람: 돌아온 줄. #line:ln_z9\n===\n", "ch01_s01.retired.yarn");

        ComposeResult result = LayerComposer.Compose(One(dialogue), One(Staging), shelf);

        Assert.False(result.Ok);
        Assert.All(result.Errors, error => Assert.Contains("없던 문제가 생긴다", error));
        Assert.Empty(result.Game);
    }

    [Theory]
    [InlineData("title: T\r\n---\r\n<<at start face s1 05>>\r\n가: 하나 #line:t1\r\n===\r\n")]
    [InlineData("title: T\n---\n가: 하나 #line:t1\n\n<<fade_out s1 10fr>>")]
    [InlineData("// 머리뿐인 파일\n")]
    [InlineData("")]
    public void 층을_읽고_그대로_다시_쓰면_바이트가_같다(string text)
        => Assert.Equal(text, LayerScript.Parse(text).ToText());

    [Fact]
    public void 시험의_대본과_데모_대본도_읽고_다시_쓰면_같다()
    {
        Assert.Equal(Scripts.Sample, LayerScript.Parse(Scripts.Sample).ToText());
        Assert.Equal(Scripts.Demo, LayerScript.Parse(Scripts.Demo).ToText());

        // 줄 ID가 없는 대사 줄은 뼈대 줄이다 — 줄 ID로 맞출 수 없다.
        LayerNode node = Assert.Single(LayerScript.Parse(Scripts.Sample).Nodes);
        Assert.Equal(new string?[] { "line:t1", "line:t2", "line:t3", "line:t4", null }, node.Items.Select(i => i.LineId));
    }

    [Theory]
    [InlineData("가: [beat=in/]하나 [beat=a/]둘 [beat=b/]셋")]
    [InlineData("[beat=in/]'화자 없는 글.'")]
    [InlineData("가람: 잘 부탁드립니다, [beat=a/]정말로요.[beat=z/]")]
    [InlineData("가: [ beat = \"a b\" / ] 따옴표 이름")]
    [InlineData("가: [b]서식[/b]은 박자가 아니다 [beat=a/]")]
    [InlineData("가: 닫히지 않은 [beat=\"a/] 표시")]
    public void 박자_표시를_가르는_법은_코어와_같다(string text)
    {
        List<BeatMarker> markers = [];
        string plain = YarnCommandTextExtractor.StripLineMarkers(text, markers);
        LineText line = LineText.Parse(text + " #auto=2s #line:x1\n");

        Assert.Equal(text, line.Text);
        Assert.Equal(" #auto=2s #line:x1", line.Tail);
        Assert.Equal("line:x1", line.LineId);
        Assert.Equal(plain, line.Plain);
        Assert.Equal(markers.Select(m => m.Position), line.Beats.Select(b => b.Position));
        Assert.Equal(YarnCommandTextExtractor.CharacterPrefixLength(plain), line.BodyStart);
    }

    // ── 박자 놓기 (K8 I5) ───────────────────────────────────────────────────

    [Fact]
    public void 박자를_옮기면_글은_그대로이고_표지만_움직인다()
    {
        const string line = "가람: 정말 잘 부탁드려요.[beat=a/] #auto=2s #line:ln_a2\n";

        BeatLine read = BeatPlacement.Read(line);
        Assert.Equal("가람", read.Speaker);
        Assert.Equal("정말 잘 부탁드려요.", read.Body);
        Assert.Equal(new BeatSpot(0, "a", 11, Movable: true), Assert.Single(read.Beats));

        // 셋째 글자("잘") 앞 — 화자를 뺀 글에서 3.
        Assert.Equal("가람: 정말 [beat=a/]잘 부탁드려요. #auto=2s #line:ln_a2\n", BeatPlacement.Move(line, 0, 3));

        // 다시 글 끝으로.
        Assert.Equal(line, BeatPlacement.Move("가람: 정말 [beat=a/]잘 부탁드려요. #auto=2s #line:ln_a2\n", 0, 11));

        // 글 첫머리에는 놓지 않고, 글 밖도 없는 박자도 아니다.
        Assert.Null(BeatPlacement.Move(line, 0, 0));
        Assert.Null(BeatPlacement.Move(line, 0, 12));
        Assert.Null(BeatPlacement.Move(line, 1, 3));
    }

    [Fact]
    public void 글_첫머리의_박자는_옮기지_않고_다른_박자를_옮겨도_제자리다()
    {
        const string line = "가람: [beat=in/]하나 둘 [beat=a/]셋 #line:t1";

        BeatLine read = BeatPlacement.Read(line);
        Assert.Equal(new[] { false, true }, read.Beats.Select(b => b.Movable));
        Assert.Equal(new[] { 0, 5 }, read.Beats.Select(b => b.Position));

        Assert.Null(BeatPlacement.Move(line, 0, 2));
        Assert.Equal("가람: [beat=in/]하나 [beat=a/]둘 셋 #line:t1", BeatPlacement.Move(line, 1, 3));

        // 옮긴 줄의 글은 코어가 읽어도 같다.
        List<BeatMarker> markers = [];
        Assert.Equal("가람: 하나 둘 셋", YarnCommandTextExtractor.StripLineMarkers("가람: [beat=in/]하나 [beat=a/]둘 셋", markers));
        Assert.Equal(new[] { 4, 7 }, markers.Select(m => m.Position));
    }

    // ── 보관소 (K8 I6) ──────────────────────────────────────────────────────

    [Fact]
    public void 보관소의_줄을_읽고_다른_줄에_붙일_글을_짓는다()
    {
        string shelf = "title: ch01_s01_ep01\n---\n" + LeadA2 + LineA2 + "===\n";

        ShelvedLine line = Assert.Single(Shelf.Read(shelf));
        Assert.Equal("line:ln_a2", line.LineId);
        Assert.Equal("ch01_s01_ep01", line.Node);
        Assert.Equal("가람", line.Speaker);
        Assert.Equal("잘 부탁드립니다, 정말로요.", line.Body);
        Assert.Equal(3, line.Commands);

        // 묶음은 머리의 빈 줄을 두고 오고, 묶음이 가리키는 박자 a는 그 줄의 글 끝에 선다. 글은 그대로다.
        (string lead, string moved) = Shelf.Onto("나래: [beat=in/]새로 쓴 줄이에요. #line:ln_b1\n", line);
        Assert.Equal("<<at start face s1 05>>\n<<at a face_swap s1 03 13fr>>\n<<at end emoji s1 star>>\n", lead);
        Assert.Equal("나래: [beat=in/]새로 쓴 줄이에요.[beat=a/] #line:ln_b1", moved);

        // 그 줄에 이미 있는 박자는 더하지 않는다.
        Assert.Equal("나래: 새로 [beat=a/]쓴 줄. #line:ln_b1", Shelf.Onto("나래: 새로 [beat=a/]쓴 줄. #line:ln_b1", line).Line);

        // 뺀 보관소 — 남은 것이 없으면 빈 글이다.
        Assert.Equal(string.Empty, Shelf.Without(shelf, "line:ln_a2"));
        Assert.Equal(shelf, Shelf.Without(shelf, "line:ln_zz"));
    }
}
