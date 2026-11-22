using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Authoring.Tests;

public class LineEditorTests
{
    private static CommandAddress At(string line, string anchor, CommandKind kind, int index)
        => new(YarnLineReader.NormalizeLineId(line), anchor, kind, index);

    private static EditOutcome Ok(string text, LineEdit edit, StageCommandSpecTable? specs = null)
    {
        EditOutcome outcome = LineEditor.Apply(text, edit, specs);
        Assert.True(outcome.Ok, outcome.Error);
        return outcome;
    }

    private static string Refused(string text, LineEdit edit, StageCommandSpecTable? specs = null)
    {
        EditOutcome outcome = LineEditor.Apply(text, edit, specs);
        Assert.False(outcome.Ok);
        Assert.False(outcome.Changed);
        Assert.Equal(text, outcome.Text);
        Assert.NotEqual(string.Empty, outcome.Error);
        return outcome.Error;
    }

    /// <summary>고친 글이 받은 글에서 줄 하나만 다르다 — 그 줄을 빼면 글자 하나까지 같다.</summary>
    private static void OneLineDiffers(string before, string after, string? removed, string? added)
    {
        List<string> a = before.Split('\n').ToList();
        List<string> b = after.Split('\n').ToList();

        if (removed is not null)
        {
            int index = a.FindIndex(l => l.Trim() == removed);
            Assert.True(index >= 0, $"받은 글에 '{removed}'가 없다");
            a.RemoveAt(index);
        }

        if (added is not null)
        {
            int index = b.FindIndex(l => l.Trim() == added);
            Assert.True(index >= 0, $"고친 글에 '{added}'가 없다");
            b.RemoveAt(index);
        }

        Assert.Equal(a, b);
    }

    // ── 값 바꾸기 ───────────────────────────────────────────────────────────

    [Fact]
    public void 값_바꾸기는_그_줄의_인자만_바꾸고_줄_끝_주석을_지킨다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new SetValue(At("t1", "a", CommandKind.Expression, 0), "face", "07"));

        Assert.True(outcome.Changed);
        Assert.Contains("<<at a face_swap s2 07 13fr>> // 줄 끝 주석\n", outcome.Text);
        OneLineDiffers(Scripts.Sample, outcome.Text, "<<at a face_swap s2 03 13fr>> // 줄 끝 주석", "<<at a face_swap s2 07 13fr>> // 줄 끝 주석");
        Assert.Equal(At("t1", "a", CommandKind.Expression, 0), outcome.Target);
        Assert.Equal("07", Scripts.Read(outcome.Text, "t1").Step("a").Commands[0].Bound[1].Value);
    }

    [Fact]
    public void 같은_값으로_고치면_바뀌지_않는다()
    {
        EditOutcome written = Ok(Scripts.Sample, new SetValue(At("t1", "a", CommandKind.Expression, 0), "face", "03"));
        Assert.False(written.Changed);
        Assert.Equal(Scripts.Sample, written.Text);

        // 적지 않은 칸을 생략값과 같은 값으로 — 뜻이 같으므로 적지 않는다.
        string text = Ok(Scripts.Sample, new AddCommand("t1", "start", "fade", ["s1"])).Text;
        EditOutcome omitted = Ok(text, new SetValue(At("t1", "start", CommandKind.Placement, 0), "duration", "18fr"));
        Assert.False(omitted.Changed);
    }

    [Fact]
    public void 뒤_칸을_적으면_적지_않은_앞_칸은_생략값으로_채운다()
    {
        string text = Ok(Scripts.Sample, new AddCommand("t1", "start", "fade", ["s1"])).Text;
        EditOutcome outcome = Ok(text, new SetValue(At("t1", "start", CommandKind.Placement, 0), "duration", "10fr"));

        Assert.Equal("<<at start fade s1 1 10fr>>", outcome.Added);
    }

    [Fact]
    public void 생략값이_없는_앞_칸이_비어_있으면_뒤_칸을_적지_않는다()
    {
        StageCommandSpecTable specs = StageCommandSpecTable.Core.With(
        [
            new StageCommandSpec("probe", CommandKind.Screen, "시험", new[]
            {
                new CommandArgSpec("slot", "슬롯", ArgKind.Slot),
                new CommandArgSpec("ease", "이징", ArgKind.Ease, ""),
                new CommandArgSpec("duration", "길이", ArgKind.Duration, "1s"),
            }),
        ]);

        string text = Ok(Scripts.Sample, new AddCommand("t1", "start", "probe", ["s1"]), specs).Text;
        string error = Refused(text, new SetValue(At("t1", "start", CommandKind.Screen, 0), "duration", "2s"), specs);

        Assert.Contains("ease", error);
    }

    [Fact]
    public void 칸을_비우면_맨_뒤의_적은_칸이_사라진다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new SetValue(At("t1", "a", CommandKind.Expression, 0), "duration", null));

        Assert.Equal("<<at a face_swap s2 03>> // 줄 끝 주석", outcome.Added);
        Assert.Equal("13fr", Scripts.Read(outcome.Text, "t1").Step("a").Commands[0].Bound[2].Value);
    }

    [Fact]
    public void 꼭_적는_칸은_비우지_않는다()
    {
        string error = Refused(Scripts.Sample, new SetValue(At("t1", "a", CommandKind.Expression, 0), "slot", null));
        Assert.Contains("slot", error);
    }

    [Fact]
    public void 낱말_칸은_적으면_자리를_차지하고_비우면_뒤가_당겨진다()
    {
        string text = Ok(Scripts.Sample, new AddCommand("t1", "start", "size", ["s1", "front", "10fr"])).Text;
        CommandAddress size = At("t1", "start", CommandKind.Placement, 0);

        EditOutcome kept = Ok(text, new SetValue(size, "keep_y", "keep_y"));
        Assert.Equal("<<at start size s1 front keep_y 10fr>>", kept.Added);

        EditOutcome cleared = Ok(kept.Text, new SetValue(size, "keep_y", null));
        Assert.Equal("<<at start size s1 front 10fr>>", cleared.Added);
        Assert.Equal(text, cleared.Text);
    }

    [Theory]
    [InlineData("duration", "빠르게")]
    [InlineData("face", "a b")]
    [InlineData("face", "x>>")]
    [InlineData("없는칸", "1")]
    public void 읽지_못할_값과_없는_칸은_거절한다(string arg, string value)
        => Refused(Scripts.Sample, new SetValue(At("t1", "a", CommandKind.Expression, 0), arg, value));

    [Fact]
    public void 고를_수_있는_낱말이_정해진_칸은_그_낱말만_받는다()
    {
        string text = Ok(Scripts.Sample, new AddCommand("t1", "start", "motion", ["s1", "nod"])).Text;
        CommandAddress motion = At("t1", "start", CommandKind.Acting, 0);

        Refused(text, new SetValue(motion, "mode", "sideways"));
        Assert.Equal("<<at start motion s1 nod absolute>>", Ok(text, new SetValue(motion, "mode", "absolute")).Added);
    }

    // ── 커맨드 더하기 ───────────────────────────────────────────────────────

    [Fact]
    public void 더하기는_그_앵커의_맨_뒤에_붙인다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new AddCommand("t1", "in", "fade", ["s2", "0.7", "15fr"]));

        // in의 마지막 커맨드(face) 다음, 제 간격 줄 앞이다.
        Assert.Contains("<<at in face s2 05>>\n<<at in fade s2 0.7 15fr>>\n<<gap in 1s>>\n", outcome.Text);
        OneLineDiffers(Scripts.Sample, outcome.Text, null, "<<at in fade s2 0.7 15fr>>");
        Assert.Equal(At("t1", "in", CommandKind.Placement, 1), outcome.Target);

        // 앞의 pause가 붙잡은 만큼 늦게 시작한다.
        Assert.Equal("in fade s2 0.7 15fr +0.5", Scripts.Commands(outcome.Line!)[3]);
    }

    [Fact]
    public void 순번을_주면_그_종류의_그_자리에_끼운다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new AddCommand("t1", "in", "fade", ["s2"], KindIndex: 0));

        Assert.Contains("<<at in fade s2>>\n<<at in fade_out s1 15fr>>\n", outcome.Text);
        Assert.Equal(At("t1", "in", CommandKind.Placement, 0), outcome.Target);

        Refused(Scripts.Sample, new AddCommand("t1", "in", "fade", ["s2"], KindIndex: 2));
    }

    [Fact]
    public void 커맨드가_없는_표지에_더하면_뒤_앵커의_줄_앞에_선다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new AddCommand("t1", "b", "motion", ["s2", "nod"]));

        Assert.Contains("<<at b motion s2 nod>>\n<<at end emoji s2 star>>\n", outcome.Text);
        Assert.Equal(new[] { "in", "start", "a", "b", "end" }, outcome.Line!.Steps.Select(s => s.Name));
    }

    [Fact]
    public void 묶음이_없는_줄에_더하면_대사_줄_바로_위에_선다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new AddCommand("t4", "start", "face", ["s1", "9"]));

        Assert.Contains("    나: 들여쓴 줄 #line:t3\n<<at start face s1 9>>\n다: 묶음이 없는 줄 #line:t4\n", outcome.Text);
        OneLineDiffers(Scripts.Sample, outcome.Text, null, "<<at start face s1 9>>");
    }

    [Fact]
    public void 새_줄은_대사_줄의_들여쓰기를_따른다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new AddCommand("t3", "end", "emoji", ["s1", "star"]));

        Assert.Contains("    <<at start face s1 3>>\n    <<at end emoji s1 star>>\n    나: 들여쓴 줄 #line:t3\n", outcome.Text);
    }

    [Fact]
    public void 빈_줄과_주석이_대사_줄_위에_있으면_새_줄은_마지막_커맨드_다음에_선다()
    {
        const string text = "title: T\n---\n<<at start face s1 3>>\n// 메모\n\n가: 하나 #line:t1\n===\n";
        EditOutcome outcome = Ok(text, new AddCommand("t1", "end", "emoji", ["s1", "star"]));

        Assert.Equal("title: T\n---\n<<at start face s1 3>>\n<<at end emoji s1 star>>\n// 메모\n\n가: 하나 #line:t1\n===\n", outcome.Text);
    }

    [Fact]
    public void 더하기를_거절하는_경우()
    {
        Assert.Contains("표지", Refused(Scripts.Sample, new AddCommand("t1", "zz", "face", ["s1", "3"])));
        Assert.Contains("명세 표", Refused(Scripts.Sample, new AddCommand("t1", "start", "없는명령", [])));
        Assert.Contains("바로 실행", Refused(Scripts.Sample, new AddCommand("t1", "start", "surface_reset", [])));
        Assert.Contains("face", Refused(Scripts.Sample, new AddCommand("t1", "start", "face", ["s1"])));
        Refused(Scripts.Sample, new AddCommand("t1", "start", "face", ["s1", "3", "넘침"]));
    }

    // ── 커맨드 빼기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 빼기는_그_줄_하나만_지운다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new RemoveCommand(At("t1", "in", CommandKind.Expression, 0)));

        OneLineDiffers(Scripts.Sample, outcome.Text, "<<at in face s2 05>>", null);
        Assert.Null(outcome.Target);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void 표지의_마지막_커맨드를_빼면_알리고_받아들인다()
    {
        // 표지는 대사 글의 것이라 이 툴이 지우지 못한다 — 원문 검사에서 경고다(AR7).
        EditOutcome outcome = Ok(Scripts.Sample, new RemoveCommand(At("t1", "a", CommandKind.Expression, 0)));

        Assert.Single(outcome.Warnings);
        Assert.Contains("[beat=a/]", outcome.Warnings[0]);
        Assert.Null(outcome.Line!.Step("a"));
    }

    [Fact]
    public void 없는_주소는_거절한다()
    {
        Refused(Scripts.Sample, new RemoveCommand(At("t1", "a", CommandKind.Expression, 1)));
        Refused(Scripts.Sample, new RemoveCommand(At("t1", "a", CommandKind.Camera, 0)));
        Refused(Scripts.Sample, new RemoveCommand(At("t1", "b", CommandKind.Expression, 0)));
        Refused(Scripts.Sample, new RemoveCommand(At("없는줄", "a", CommandKind.Expression, 0)));
    }

    // ── 지연 바꾸기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 지연을_주면_그_커맨드_앞에_pause가_선다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new SetDelay(At("t1", "a", CommandKind.Expression, 0), "12fr"));

        Assert.Contains("<<at a pause 12fr>>\n<<at a face_swap s2 03 13fr>> // 줄 끝 주석\n", outcome.Text);
        OneLineDiffers(Scripts.Sample, outcome.Text, null, "<<at a pause 12fr>>");
        Assert.Equal(At("t1", "a", CommandKind.Expression, 0), outcome.Target);
        Assert.Equal(0.4f, outcome.Line!.Step("a").Commands[1].Delay, 3);
    }

    [Fact]
    public void 앞에_pause가_있으면_그_길이를_고친다()
    {
        // in: fade_out · pause 15fr · face — face의 지연은 0.5초다.
        EditOutcome outcome = Ok(Scripts.Sample, new SetDelay(At("t1", "in", CommandKind.Expression, 0), "0.8s"));

        OneLineDiffers(Scripts.Sample, outcome.Text, "<<at in pause 15fr>>", "<<at in pause 0.8s>>");
        Assert.Equal(0.8f, outcome.Line!.Step("in").Commands[2].Delay, 3);
    }

    [Fact]
    public void 지연을_0으로_하면_앞의_pause를_뺀다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new SetDelay(At("t1", "in", CommandKind.Expression, 0), "0fr"));

        OneLineDiffers(Scripts.Sample, outcome.Text, "<<at in pause 15fr>>", null);
        Assert.Equal(At("t1", "in", CommandKind.Expression, 0), outcome.Target);
        Assert.Equal(0f, outcome.Line!.Step("in").Commands[1].Delay, 3);
    }

    [Fact]
    public void 같은_지연이면_바뀌지_않는다()
    {
        Assert.False(Ok(Scripts.Sample, new SetDelay(At("t1", "in", CommandKind.Expression, 0), "0.5s")).Changed);
        Assert.False(Ok(Scripts.Sample, new SetDelay(At("t1", "a", CommandKind.Expression, 0), "0s")).Changed);
    }

    [Fact]
    public void 앞선_기다림보다_당길_수는_없다()
    {
        // in에 커맨드를 하나 더 붙이면 그 지연의 바닥은 앞의 pause(0.5초)다.
        string text = Ok(Scripts.Sample, new AddCommand("t1", "in", "fade", ["s2"])).Text;
        CommandAddress fade = At("t1", "in", CommandKind.Placement, 1);

        Assert.Contains("0.5s", Refused(text, new SetDelay(fade, "0.2s")));

        EditOutcome later = Ok(text, new SetDelay(fade, "1s"));
        Assert.Equal("<<at in pause 0.5s>>", later.Added);
        Assert.Equal(1f, later.Line!.Step("in").Commands[4].Delay, 3);
    }

    [Fact]
    public void 프레임으로_준_지연은_프레임으로_적고_맞아떨어지지_않으면_초로_적는다()
    {
        Assert.Equal("15fr", LineEditor.FormatDuration(0.5f, frames: true));
        Assert.Equal("0.5s", LineEditor.FormatDuration(0.5f, frames: false));
        Assert.Equal("0.25s", LineEditor.FormatDuration(0.25f, frames: true));
    }

    // ── 간격 바꾸기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 간격을_고치고_넣고_걷는다()
    {
        EditOutcome changed = Ok(Scripts.Sample, new SetGap("t1", "in", "2s"));
        OneLineDiffers(Scripts.Sample, changed.Text, "<<gap in 1s>>", "<<gap in 2s>>");
        Assert.Equal(2f, changed.Line!.Step("in").Gap, 3);

        EditOutcome inserted = Ok(Scripts.Sample, new SetGap("t1", "a", "12fr"));
        Assert.Contains("<<at a face_swap s2 03 13fr>> // 줄 끝 주석\n<<gap a 12fr>>\n", inserted.Text);

        EditOutcome cleared = Ok(Scripts.Sample, new SetGap("t1", "in", null));
        OneLineDiffers(Scripts.Sample, cleared.Text, "<<gap in 1s>>", null);
        Assert.False(cleared.Line!.Step("in").HasGap);
    }

    [Fact]
    public void 커맨드가_없는_표지에도_간격을_둘_수_있다()
    {
        EditOutcome outcome = Ok(Scripts.Sample, new SetGap("t1", "b", "1s"));

        Assert.Contains("<<gap b 1s>>\n<<at end emoji s2 star>>\n", outcome.Text);
        Assert.Equal(1f, outcome.Line!.Step("b").Gap, 3);
    }

    [Fact]
    public void 같은_간격과_없는_간격_걷기는_바뀌지_않고_없는_앵커는_거절한다()
    {
        Assert.False(Ok(Scripts.Sample, new SetGap("t1", "in", "30fr")).Changed);
        Assert.False(Ok(Scripts.Sample, new SetGap("t1", "a", null)).Changed);
        Assert.False(Ok(Scripts.Sample, new SetGap("t1", "a", "0s")).Changed);
        Refused(Scripts.Sample, new SetGap("t1", "zz", "1s"));
        Refused(Scripts.Sample, new SetGap("t1", "a", "길게"));
    }

    // ── 고치지 않는 줄 ──────────────────────────────────────────────────────

    [Fact]
    public void at_없는_명령이_든_줄은_읽기만_하고_까닭에_그_명령을_적는다()
    {
        string error = Refused(Scripts.Sample, new AddCommand("t2", "start", "face", ["s1", "3"]));
        Assert.Contains("at 없는 명령", error);
        Assert.Contains("fade_out", error);
        Assert.DoesNotContain("<<", error);     // 화면(DirectorText)이 <<…>>를 명령 이름으로 바꾼다 — 까닭은 그 꼴을 쓰지 않는다
        Assert.False(LineEditor.IsEditable(Scripts.Read(Scripts.Sample, "t2"), out _));
        Assert.True(LineEditor.IsEditable(Scripts.Read(Scripts.Sample, "t1"), out _));
    }

    [Fact]
    public void 걷힌_step이_남은_줄은_읽기만_한다()
    {
        // 런타임 N5 — <<step>>은 등록이 없는 명령이다. at 없는 명령의 하나로 잡힌다.
        const string text = "title: T\n---\n<<step>>\n<<at start face s1 3>>\n가: 하나 #line:u1\n===\n";
        Assert.Contains("step", Refused(text, new AddCommand("u1", "start", "face", ["s2", "3"])));
    }

    [Fact]
    public void 노드_끝의_at_없는_명령은_마지막_줄을_막지_않는다()
    {
        // 붙을 줄이 없이 남은 명령은 암묵 스텝이다 — 어느 줄의 묶음에도 들지 않는다.
        const string text = "title: T\n---\n<<at start face s1 3>>\n가: 하나 #line:u1\n<<fade_out s1 15fr>>\n===\n";
        EditOutcome outcome = Ok(text, new AddCommand("u1", "end", "emoji", ["s1", "star"]));

        Assert.Equal("title: T\n---\n<<at start face s1 3>>\n<<at end emoji s1 star>>\n가: 하나 #line:u1\n<<fade_out s1 15fr>>\n===\n", outcome.Text);
    }

    [Fact]
    public void LineId가_둘인_대본은_고치지_않는다()
    {
        string text = Scripts.Sample.Replace("#line:t4", "#line:t3");
        Assert.Contains("둘", Refused(text, new AddCommand("t3", "start", "face", ["s1", "3"])));
    }

    [Fact]
    public void 앵커는_열리는_차례로_나오고_커맨드가_없는_표지도_든다()
    {
        Assert.Equal(new[] { "in", "start", "a", "b", "end" }, LineEditor.Anchors(Scripts.Sample, Scripts.Read(Scripts.Sample, "t1")));
        Assert.Equal(new[] { "start", "end" }, LineEditor.Anchors(Scripts.Sample, Scripts.Read(Scripts.Sample, "t4")));
    }

    // ── 파일의 나머지 ───────────────────────────────────────────────────────

    public static IEnumerable<object[]> DemoEdits()
    {
        CommandAddress a = At(Scripts.DemoLine, "a", CommandKind.Expression, 0);

        yield return [new SetValue(a, "face", "07")];
        yield return [new AddCommand(Scripts.DemoLine, "end", "emoji", ["s1", "star"])];
        yield return [new AddCommand(Scripts.DemoLine, "start", "shot_zoom", ["1.2"], 0)];
        yield return [new RemoveCommand(a)];
        yield return [new SetDelay(a, "15fr")];
        yield return [new SetGap(Scripts.DemoLine, "a", "1s")];
    }

    [Theory]
    [MemberData(nameof(DemoEdits))]
    public void 데모_대본에서_고친_줄의_묶음_밖은_글자_하나_바뀌지_않는다(LineEdit edit)
    {
        foreach (string text in new[] { Scripts.Demo, Scripts.Crlf(Scripts.Demo) })
        {
            YarnFileRead before = YarnLineReader.ReadFile(text);
            before.TryGet(Scripts.DemoLine, out LineRead line);

            EditOutcome outcome = Ok(text, edit);
            Assert.True(outcome.Changed);

            // 묶음의 앞과 뒤(대사 줄부터 파일 끝까지)가 그대로다.
            string head = text[..line.Block.Start];
            string tail = text[(line.Block.Start + line.Block.Length)..];
            Assert.StartsWith(head, outcome.Text);
            Assert.EndsWith(tail, outcome.Text);

            // 묶음 안에서도 한 줄만 다르다.
            OneLineDiffers(text, outcome.Text, outcome.Removed, outcome.Added);

            // 줄 끝 문자가 섞이지 않는다.
            Assert.Equal(text.Contains("\r\n"), outcome.Text.Contains("\r\n"));
            Assert.DoesNotContain("\n", outcome.Text.Replace("\r\n", string.Empty).Replace(text.Contains("\r\n") ? "\r\n" : "\n", string.Empty));

            // 고치지 않은 줄의 커맨드 열이 같다.
            YarnFileRead after = YarnLineReader.ReadFile(outcome.Text);
            Assert.Equal(before.Lines.Count, after.Lines.Count);

            for (int i = 0; i < before.Lines.Count; i++)
            {
                if (before.Lines[i].LineId != line.LineId)
                {
                    Assert.Equal(Scripts.Commands(before.Lines[i]), Scripts.Commands(after.Lines[i]));
                }
            }
        }
    }

    [Fact]
    public void 데모_대본은_어느_줄이나_고칠_수_있다()
    {
        // 런타임 417bf41e(N5 · 계획서 U4) — 데모의 대사 줄 116개 모두 LineId가 있고 at 없는 명령이 없다.
        // 줄마다 커맨드 하나를 더해 본다 — 줄 고치기가 다시 읽어 새 문제 · 다른 줄의 달라짐을 본다.
        string text = Scripts.Demo;
        YarnFileRead file = YarnLineReader.ReadFile(text);

        Assert.Equal(116, file.Lines.Count);

        foreach (LineRead line in file.Lines)
        {
            Assert.True(LineEditor.IsEditable(line, out string reason), $"{line.LineId}: {reason}");
            Assert.True(Ok(text, new AddCommand(line.LineId!, "end", "emoji", ["s1", "star"])).Changed, line.LineId);
        }
    }

    [Fact]
    public void 대사_줄이_파일의_마지막_줄이어도_줄바꿈을_맞춘다()
    {
        const string text = "title: T\r\n---\r\n가: 하나 #line:t1";
        EditOutcome outcome = Ok(text, new AddCommand("t1", "start", "face", ["s1", "3"]));

        Assert.Equal("title: T\r\n---\r\n<<at start face s1 3>>\r\n가: 하나 #line:t1", outcome.Text);
    }
}
