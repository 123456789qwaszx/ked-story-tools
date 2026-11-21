using Ked.Presentation.Core;
using St.Authoring.Reading;
using St.Authoring.Scripts;

namespace St.Authoring.Editing;

/// <summary>
/// 열어 둔 대본을 고치는 자리 — 화면이 쥔다(docs/PLAN.md U1).
///
///   조작마다 바로 파일에 쓴다 — Unity에서 되감아 바로 보는 것이 고치는 고리다. 되돌리기 · 다시 하기도 파일에 쓴다.
///   되돌리기는 파일을 넘나드는 한 줄이다 — 조작 하나가 한 칸.
///   바깥에서 파일이 바뀌었으면 그 파일의 되돌리기를 비우고 디스크의 글에서 다시 시작한다 — 바깥의 고침을 덮어쓰지 않는다.
///
/// 고치는 길은 <see cref="LineEditor"/> 하나다 — 여기는 파일과 되돌리기의 살림만 한다.
/// </summary>
public sealed class EditSession
{
    private readonly StageCommandSpecTable? _specs;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];

    private EditSession(string root, StageCommandSpecTable? specs, ScriptLibrary library)
    {
        Root = root;
        _specs = specs;
        Library = library;
        Sync();
    }

    /// <summary>연 경로 — 파일이나 폴더.</summary>
    public string Root { get; }

    /// <summary>지금 디스크의 대본을 읽은 것. 고칠 때마다 새로 읽는다.</summary>
    public ScriptLibrary Library { get; private set; }

    /// <summary>대본이 바뀔 때마다 하나 오른다 — 조작 · 되돌리기 · 다시 하기 · 바깥의 고침을 읽어 들였을 때.</summary>
    public int Revision { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>파일이나 폴더를 연다.</summary>
    public static EditSession Open(string path, StageCommandSpecTable? specs = null)
        => new(path, specs, ScriptLibrary.Open(path, specs));

    /// <summary>그 파일의 줄에 조작 하나를 하고, 바뀌었으면 바로 파일에 쓴다.</summary>
    public EditOutcome Apply(string path, LineEdit edit) => Apply(path, [edit]);

    /// <summary>그 파일에 조작 여럿을 한 덩이로 한다 — 모두 받아들여져야 쓰고, 되돌리기 한 칸이다.</summary>
    public EditOutcome Apply(string path, IReadOnlyList<LineEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(edits);

        FileState state;

        try
        {
            state = Current(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return EditOutcome.Refused(string.Empty, $"파일을 읽지 못했다 — {exception.Message}");
        }

        EditOutcome outcome = state.Editor.Apply(edits);

        if (outcome is not { Ok: true, Changed: true })
        {
            return outcome;
        }

        if (Save(state) is { } failure)
        {
            state.Editor.Undo();
            return EditOutcome.Refused(state.Editor.Text, failure);
        }

        _undo.Add(path);
        _redo.Clear();
        Reload();
        return outcome;
    }

    /// <summary>마지막 조작을 되돌려 파일에 쓴다. 되돌릴 것이 없으면 false.</summary>
    public bool Undo() => Step(_undo, _redo, editor => editor.Undo());

    /// <summary>되돌린 조작을 다시 해 파일에 쓴다.</summary>
    public bool Redo() => Step(_redo, _undo, editor => editor.Redo());

    /// <summary>
    /// 디스크를 다시 읽는다 — 바깥에서 바뀐 파일이 있으면 그 파일의 되돌리기를 비운다. 바뀐 것이 있었으면 true.
    /// </summary>
    public bool Refresh()
    {
        int before = Revision;
        ScriptLibrary fresh = ScriptLibrary.Open(Root, _specs);
        bool changed = fresh.Files.Count != Library.Files.Count
            || fresh.Files.Any(f => !_files.TryGetValue(f.Script.Path, out FileState? state) || state.Editor.Text != f.Script.Text);

        if (changed)
        {
            Library = fresh;
            Sync();
            Revision++;
        }

        return Revision != before;
    }

    /// <summary>이 줄이 연 뒤로 고쳐졌는가 — 그 줄의 커맨드 묶음이 처음 읽은 것과 다르다.</summary>
    public bool IsEdited(LibraryLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Read.LineId is null
            || !_files.TryGetValue(line.File.Script.Path, out FileState? state)
            || !state.OriginalRead.TryGet(line.Read.LineId, out LineRead original))
        {
            return false;
        }

        return !Block(state.Original, original).SequenceEqual(Block(line.File.Script.Text, line.Read));
    }

    private static ReadOnlySpan<char> Block(string text, LineRead line) => text.AsSpan(line.Block.Start, line.Block.Length);

    private bool Step(List<string> from, List<string> to, Func<StagingEditor, bool> move)
    {
        while (from.Count > 0)
        {
            string path = from[^1];
            from.RemoveAt(from.Count - 1);

            FileState state;

            try
            {
                state = Current(path);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                continue;
            }

            // 바깥에서 바뀐 파일은 Current가 되돌리기를 비웠다 — 그 칸은 건너뛴다.
            if (!move(state.Editor))
            {
                continue;
            }

            if (Save(state) is not null)
            {
                Forget(path);
                Reload();
                return false;
            }

            to.Add(path);
            Reload();
            return true;
        }

        return false;
    }

    /// <summary>그 파일의 지금 상태 — 디스크의 글이 우리가 마지막에 쓴 것과 다르면 디스크에서 다시 시작한다.</summary>
    private FileState Current(string path)
    {
        ScriptFile disk = ScriptFile.Load(path);

        if (_files.TryGetValue(path, out FileState? state) && state.Editor.Text == disk.Text)
        {
            return state;
        }

        Forget(path);
        state = new FileState(disk, _specs);
        _files[path] = state;
        return state;
    }

    private void Forget(string path)
    {
        _files.Remove(path);
        _undo.RemoveAll(p => p == path);
        _redo.RemoveAll(p => p == path);
    }

    private static string? Save(FileState state)
    {
        try
        {
            state.Script.Save(state.Editor.Text);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"파일에 쓰지 못했다 — {exception.Message}";
        }
    }

    private void Reload()
    {
        Library = ScriptLibrary.Open(Root, _specs);
        Sync();
        Revision++;
    }

    // 읽은 대본과 파일의 상태를 맞춘다 — 처음 보는 파일은 지금의 글이 "처음 읽은 것"이고,
    // 우리가 쓴 것과 다른 글이 디스크에 있으면 바깥에서 바뀐 것이다.
    private void Sync()
    {
        foreach (LibraryFile file in Library.Files)
        {
            string path = file.Script.Path;

            if (_files.TryGetValue(path, out FileState? state) && state.Editor.Text == file.Script.Text)
            {
                continue;
            }

            Forget(path);
            _files[path] = new FileState(file.Script, _specs);
        }
    }

    private sealed class FileState(ScriptFile script, StageCommandSpecTable? specs)
    {
        public ScriptFile Script { get; } = script;

        public StagingEditor Editor { get; } = new(script.Text, specs);

        /// <summary>이 파일을 처음 읽은(또는 바깥에서 바뀐 뒤 다시 읽은) 글 — 고친 줄을 가리는 기준.</summary>
        public string Original { get; } = script.Text;

        public YarnFileRead OriginalRead { get; } = YarnLineReader.ReadFile(script.Text, script.Path, specs);
    }
}
