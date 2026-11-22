using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Authoring.Tests;

public class StagingEditorTests
{
    private static readonly CommandAddress FaceSwap = new("line:t1", "a", CommandKind.Expression, 0);

    [Fact]
    public void 조작_하나가_되돌리기_한_칸이다()
    {
        StagingEditor editor = new(Scripts.Sample);

        Assert.True(editor.Apply(new SetValue(FaceSwap, "face", "07")).Changed);
        Assert.True(editor.Apply(new SetGap("t1", "a", "1s")).Changed);
        Assert.Equal(2, editor.Revision);
        string edited = editor.Text;

        Assert.True(editor.Undo());
        Assert.Contains("face_swap s2 07", editor.Text);
        Assert.DoesNotContain("<<gap a", editor.Text);

        Assert.True(editor.Undo());
        Assert.Equal(Scripts.Sample, editor.Text);
        Assert.False(editor.Undo());

        Assert.True(editor.Redo());
        Assert.True(editor.Redo());
        Assert.Equal(edited, editor.Text);
        Assert.False(editor.Redo());
    }

    [Fact]
    public void 같은_값과_거절은_기록하지_않는다()
    {
        StagingEditor editor = new(Scripts.Sample);

        Assert.False(editor.Apply(new SetValue(FaceSwap, "face", "03")).Changed);
        Assert.False(editor.Apply(new SetValue(FaceSwap, "duration", "빠르게")).Ok);

        Assert.Equal(0, editor.Revision);
        Assert.False(editor.CanUndo);
        Assert.Equal(Scripts.Sample, editor.Text);
    }

    [Fact]
    public void 새_조작은_다시_하기를_비운다()
    {
        StagingEditor editor = new(Scripts.Sample);
        editor.Apply(new SetValue(FaceSwap, "face", "07"));
        editor.Undo();

        editor.Apply(new SetValue(FaceSwap, "face", "09"));

        Assert.False(editor.CanRedo);
        Assert.Equal("09", editor.Read().Lines[0].Step("a").Commands[0].Bound[1].Value);
    }
}
