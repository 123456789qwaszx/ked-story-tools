using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.Authoring.Tests;

/// <summary>화면의 손이 기대는 순수 계산 — 수치 끌기 · 복사와 붙이기 · 고치기 전후 맞대기.</summary>
public class UxTests
{
    private static CommandArgSpec Arg(string command, string arg)
    {
        StageCommandSpecTable.Default.TryGet(command, out StageCommandSpec spec);
        return spec.Args.First(a => a.Name == arg);
    }

    private static CommandAddress At(string anchor, CommandKind kind, int index) => new("line:t1", anchor, kind, index);

    // ── 수치 끌기 ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("face_swap", "duration", "13fr", 2, false, "15fr")]
    [InlineData("face_swap", "duration", "13fr", -20, false, "0fr")]
    [InlineData("face_swap", "duration", "0.4s", 2, false, "0.5s")]
    [InlineData("face_swap", "duration", "0.4s", 3, true, "0.415s")]
    [InlineData("place", "x", "-2u", 5, false, "-1.5u")]
    [InlineData("place", "x", "1.5u", -1, true, "1.49u")]
    [InlineData("rotate_by", "angle", "10", -3, false, "7")]
    [InlineData("shot_zoom", "zoom", "1.2", 2, false, "1.3")]
    [InlineData("fade", "alpha", "0.72", -2, true, "0.71")]
    public void 걸음은_갈래가_정하고_단위는_적힌_그대로다(string command, string arg, string token, int steps, bool fine, string expected)
        => Assert.Equal(expected, ValueScrub.Step(Arg(command, arg), token, steps, fine));

    [Fact]
    public void 빈_칸은_생략값에서_시작하고_수가_아닌_칸은_끌리지_않는다()
    {
        Assert.Equal("15fr", ValueScrub.Step(Arg("face_swap", "duration"), string.Empty, 2));
        Assert.Equal("0.1u", ValueScrub.Step(Arg("place", "x"), null, 1));

        Assert.False(ValueScrub.CanScrub(Arg("face_swap", "face")));
        Assert.False(ValueScrub.CanScrub(Arg("face_swap", "slot")));
        Assert.Null(ValueScrub.Step(Arg("face_swap", "face"), "05", 1));
        Assert.Null(ValueScrub.Step(Arg("shot_zoom", "zoom"), "크게", 1));
    }

    // ── 복사 · 붙이기 · 여럿 빼기 ───────────────────────────────────────────

    [Fact]
    public void 고른_커맨드를_복사해_다른_줄에_붙인다()
    {
        string text = LineEditor.Apply(Scripts.Sample, new AddCommand("t1", "start", "fade_in", ["s1", "0.6s"], Card: "E04")).Text;
        LineRead line = Scripts.Read(text, "t1");

        IReadOnlyList<CommandClip> clips = CommandClips.Copy(
            text, line, new HashSet<CommandAddress> { At("in", CommandKind.Expression, 0), At("start", CommandKind.Placement, 0), At("a", CommandKind.Expression, 0) });

        Assert.Equal(new[] { "in face", "start fade_in", "a face_swap" }, clips.Select(c => $"{c.Anchor} {c.Name}"));
        Assert.Equal(new[] { null, "E04", null }, clips.Select(c => c.Card));

        // t4에는 표지가 없다 — start의 것은 start에, 표지의 것은 고른 앵커(end)에 붙는다. 카드의 출처는 따라간다.
        EditOutcome pasted = LineEditor.ApplyAll(text, CommandClips.Paste("t4", ["start", "end"], clips, "end"));

        Assert.True(pasted.Ok, pasted.Error);
        Assert.Contains(
            "<<at start fade_in s1 0.6s>> // card:E04\n<<at end face s2 05>>\n<<at end face_swap s2 03 13fr>>\n다: 묶음이 없는 줄 #line:t4\n",
            pasted.Text);
    }

    [Fact]
    public void 같은_이름의_앵커가_있으면_거기에_붙는다()
    {
        LineRead line = Scripts.Read(Scripts.Sample, "t1");
        IReadOnlyList<CommandClip> clips = CommandClips.Copy(Scripts.Sample, line, new HashSet<CommandAddress> { At("a", CommandKind.Expression, 0) });

        AddCommand edit = (AddCommand)CommandClips.Paste("t1", ["in", "start", "a", "b", "end"], clips, "start")[0];

        Assert.Equal("a", edit.Anchor);
    }

    [Fact]
    public void 여럿을_빼면_뒤의_것부터_빠져_순번이_밀리지_않는다()
    {
        string text = LineEditor.ApplyAll(Scripts.Sample,
        [
            new AddCommand("t1", "start", "face", ["s1", "1"]),
            new AddCommand("t1", "start", "face", ["s1", "2"]),
        ]).Text;

        // start의 표정 셋(05 · 1 · 2)에서 첫째와 셋째를 뺀다.
        EditOutcome outcome = LineEditor.ApplyAll(text, CommandClips.Remove([At("start", CommandKind.Expression, 0), At("start", CommandKind.Expression, 2)]));

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(new[] { "start face s1 1 +0" }, Scripts.Commands(outcome.Line!).Where(c => c.StartsWith("start", StringComparison.Ordinal)));
    }

    [Fact]
    public void 줄의_커맨드에서_카드의_출처를_읽는다()
    {
        string text = LineEditor.ApplyAll(Scripts.Sample,
        [
            new AddCommand("t1", "start", "fade_in", ["s1"], Card: "E04"),
            new AddCommand("t1", "end", "fade_out", ["s1"], Card: "X01#2"),
        ]).Text;
        LineRead line = Scripts.Read(text, "t1");

        IReadOnlyDictionary<int, string> tags = LineEditor.CardTags(text, line);

        Assert.Equal("E04", tags[line.Step("start").Commands[1].Line]);
        Assert.Equal("X01#2", tags[line.Step("end").Commands[1].Line]);
        Assert.Equal(2, tags.Count);
    }

    // ── 고치기 전후 ─────────────────────────────────────────────────────────

    [Fact]
    public void 고치기_전과_뒤를_앵커마다_맞댄다()
    {
        LineRead before = Scripts.Read(Scripts.Sample, "t1");
        string text = LineEditor.ApplyAll(Scripts.Sample,
        [
            new SetValue(At("a", CommandKind.Expression, 0), "face", "07"),
            new SetDelay(At("in", CommandKind.Expression, 0), "1s"),
            new SetGap("t1", "in", "2s"),
            new AddCommand("t1", "end", "fade_out", ["s1"]),
        ]).Text;

        IReadOnlyList<LineChange> changes = LineDiff.Compare(before, Scripts.Read(text, "t1"));

        Assert.Equal(
            new[]
            {
                "in - face s2 05  +0.5s · 15fr",
                "in - 간격 1s · 30fr",
                "in + face s2 05  +1s · 30fr",
                "in + 간격 2s · 60fr",
                "a - face_swap s2 03 13fr",
                "a + face_swap s2 07 13fr",
                "end + fade_out s1",
            },
            changes.Select(c => $"{c.Anchor} {(c.Added ? "+" : "-")} {c.Text}"));

        Assert.Empty(LineDiff.Compare(before, before));
    }
}
