using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>
/// yarn 원문 하나를 쥐고 조작을 차례로 받는 자리 — <b>조작 하나 = 되돌리기 한 칸</b>.
/// 거절된 조작과 같은 값으로 고친 조작은 기록하지 않는다(<see cref="Revision"/>이 그대로다).
/// 파일을 모른다 — 읽고 쓰는 일은 <see cref="Scripts.ScriptFile"/>이 한다.
/// </summary>
public sealed class StagingEditor
{
    private readonly StageCommandSpecTable _specs;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public StagingEditor(string text, StageCommandSpecTable? specs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        _specs = specs ?? StageCommandSpecTable.Default;
    }

    /// <summary>지금의 yarn 원문.</summary>
    public string Text { get; private set; }

    /// <summary>글이 바뀔 때마다 하나 오른다 — 조작 · 되돌리기 · 다시 하기.</summary>
    public int Revision { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>지금의 글을 줄 읽기로 읽는다.</summary>
    public YarnFileRead Read() => YarnLineReader.ReadFile(Text, LineEditor.SourceName, _specs);

    public EditOutcome Apply(LineEdit edit) => Apply([edit]);

    /// <summary>조작 여럿을 한 덩이로 한다 — 모두 받아들여져야 바뀌고, 되돌리기 한 칸이다.</summary>
    public EditOutcome Apply(IReadOnlyList<LineEdit> edits)
    {
        EditOutcome outcome = LineEditor.ApplyAll(Text, edits, _specs);

        if (outcome is { Ok: true, Changed: true })
        {
            _undo.Push(Text);
            _redo.Clear();
            Text = outcome.Text;
            Revision++;
        }

        return outcome;
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out string? previous))
        {
            return false;
        }

        _redo.Push(Text);
        Text = previous;
        Revision++;
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out string? next))
        {
            return false;
        }

        _undo.Push(Text);
        Text = next;
        Revision++;
        return true;
    }
}
