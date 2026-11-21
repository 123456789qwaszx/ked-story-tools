using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Reading;

namespace St.Authoring.Tests;

public class EditSessionTests
{
    private static readonly CommandAddress FaceSwap = new("line:t1", "a", CommandKind.Acting, 0);

    private static (EditSession Session, string A, string B) Open()
    {
        string dir = Directory.CreateTempSubdirectory("st-session-").FullName;
        string a = Path.Combine(dir, "a.yarn");
        string b = Path.Combine(dir, "b.yarn");
        File.WriteAllText(a, Scripts.Sample);
        File.WriteAllText(b, "title: U\n---\n<<at start face s1 3>>\n마: 다른 파일의 줄 #line:u1\n===\n");
        return (EditSession.Open(dir), a, b);
    }

    private static LibraryLine Line(EditSession session, string id)
        => session.Library.Chapters.SelectMany(c => c.Scenes).SelectMany(s => s.Episodes).SelectMany(e => e.Lines).First(l => l.Id == id);

    [Fact]
    public void 조작마다_바로_파일에_쓰고_읽은_대본이_따라온다()
    {
        (EditSession session, string a, _) = Open();

        EditOutcome outcome = session.Apply(a, new SetValue(FaceSwap, "face", "07"));

        Assert.True(outcome.Changed);
        Assert.Contains("<<at a face_swap s2 07 13fr>>", File.ReadAllText(a));
        Assert.Equal("07", Line(session, "t1").Read.Step("a").Commands[0].Bound[1].Value);
        Assert.Equal(1, session.Revision);
    }

    [Fact]
    public void 되돌리기는_파일을_넘나드는_한_줄이고_파일에_쓴다()
    {
        (EditSession session, string a, string b) = Open();
        string originalB = File.ReadAllText(b);

        session.Apply(a, new SetValue(FaceSwap, "face", "07"));
        session.Apply(b, new SetGap("u1", "start", "1s"));
        session.Apply(a, new SetGap("t1", "a", "12fr"));

        Assert.True(session.Undo());
        Assert.DoesNotContain("<<gap a", File.ReadAllText(a));
        Assert.Contains("face_swap s2 07", File.ReadAllText(a));

        Assert.True(session.Undo());
        Assert.Equal(originalB, File.ReadAllText(b));

        Assert.True(session.Undo());
        Assert.Equal(Scripts.Sample, File.ReadAllText(a));
        Assert.False(session.Undo());

        Assert.True(session.Redo());
        Assert.True(session.Redo());
        Assert.Contains("<<gap start 1s>>", File.ReadAllText(b));
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void 거절과_같은_값은_쓰지도_쌓지도_않는다()
    {
        (EditSession session, string a, _) = Open();

        Assert.False(session.Apply(a, new SetValue(FaceSwap, "duration", "빠르게")).Ok);
        Assert.False(session.Apply(a, new SetValue(FaceSwap, "face", "03")).Changed);

        Assert.Equal(Scripts.Sample, File.ReadAllText(a));
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void 바깥에서_바뀐_파일은_덮어쓰지_않고_그_파일의_되돌리기를_비운다()
    {
        (EditSession session, string a, string b) = Open();
        session.Apply(a, new SetValue(FaceSwap, "face", "07"));
        session.Apply(b, new SetGap("u1", "start", "1s"));

        // 다른 도구가 a.yarn을 다시 냈다.
        string outside = Scripts.Sample.Replace("emoji s2 star", "emoji s2 heart");
        File.WriteAllText(a, outside);

        // 그 위에서 고친다 — 바깥의 고침이 남는다.
        Assert.True(session.Apply(a, new SetGap("t1", "a", "12fr")).Changed);
        string text = File.ReadAllText(a);
        Assert.Contains("emoji s2 heart", text);
        Assert.Contains("face_swap s2 03", text);

        // a의 옛 되돌리기는 사라졌고, 새 조작과 b의 것만 남는다.
        Assert.True(session.Undo());
        Assert.Equal(outside, File.ReadAllText(a));
        Assert.True(session.Undo());
        Assert.DoesNotContain("<<gap start", File.ReadAllText(b));
        Assert.False(session.Undo());
    }

    [Fact]
    public void 다시_읽으면_바깥의_고침이_들어온다()
    {
        (EditSession session, string a, _) = Open();

        Assert.False(session.Refresh());

        File.WriteAllText(a, Scripts.Sample.Replace("emoji s2 star", "emoji s2 heart"));

        Assert.True(session.Refresh());
        Assert.Equal("heart", Line(session, "t1").Read.Step("end").Commands[0].Bound[1].Value);
        Assert.False(session.IsEdited(Line(session, "t1")));
    }

    [Fact]
    public void 고친_줄을_가려_낸다()
    {
        (EditSession session, string a, _) = Open();

        Assert.False(session.IsEdited(Line(session, "t1")));

        session.Apply(a, new SetValue(FaceSwap, "face", "07"));
        Assert.True(session.IsEdited(Line(session, "t1")));
        Assert.False(session.IsEdited(Line(session, "t3")));
        Assert.False(session.IsEdited(Line(session, "u1")));

        // 되돌리면 고친 줄이 아니다 — 처음 읽은 것과 같다.
        session.Undo();
        Assert.False(session.IsEdited(Line(session, "t1")));
    }
}
