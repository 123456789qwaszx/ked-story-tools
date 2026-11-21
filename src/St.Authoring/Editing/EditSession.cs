using Ked.Presentation.Core;
using St.Authoring.Reading;
using St.Authoring.Scripts;

namespace St.Authoring.Editing;

/// <summary>
/// 열어 둔 대본을 고치는 자리 — 화면이 쥔다(docs/PLAN.md U1).
///
///   조작마다 바로 파일에 쓴다 — Unity에서 되감아 바로 보는 것이 고치는 고리다. 되돌리기 · 다시 하기도 파일에 쓴다.
///   되돌리기는 파일을 넘나드는 한 줄이다 — 조작 하나(또는 한 덩이)가 한 칸.
///   바깥에서 파일이 바뀌었으면 그 파일의 되돌리기를 비우고 디스크의 글에서 다시 시작한다 — 바깥의 고침을 덮어쓰지 않는다.
///
/// 고치는 길은 <see cref="LineEditor"/> 하나다 — 여기는 파일과 되돌리기의 살림만 한다.
/// 고칠 때마다 폴더를 다시 읽지 않는다: 바뀐 파일 하나만 다시 읽어 갈아 끼우고(<see cref="ScriptLibrary.WithFile"/>),
/// 바깥의 고침은 파일의 때와 크기로 먼저 가린다.
/// </summary>
public sealed class EditSession
{
    private readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileStamp> _stamps = new(StringComparer.Ordinal);
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];

    private EditSession(string root, StageCommandSpecTable? specs, ScriptLibrary library)
    {
        Root = root;
        Specs = specs ?? StageCommandSpecTable.Default;
        Library = library;
        Adopt();
    }

    /// <summary>연 경로 — 파일이나 폴더.</summary>
    public string Root { get; }

    /// <summary>이 대본을 읽는 명세 표 — 포크의 대본이면 포크가 낸 표다.</summary>
    public StageCommandSpecTable Specs { get; }

    /// <summary>지금의 대본. 고치면 바뀐 파일만 갈아 끼운 새것이 된다.</summary>
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
        Swap(state);
        return outcome;
    }

    /// <summary>마지막 조작을 되돌려 파일에 쓴다. 되돌릴 것이 없으면 false.</summary>
    public bool Undo() => Step(_undo, _redo, editor => editor.Undo());

    /// <summary>되돌린 조작을 다시 해 파일에 쓴다.</summary>
    public bool Redo() => Step(_redo, _undo, editor => editor.Redo());

    /// <summary>
    /// 디스크를 다시 본다 — 바깥에서 바뀐 것이 있으면 다시 읽고, 바뀐 파일의 되돌리기를 비운다. 바뀐 것이 있었으면 true.
    /// 파일의 때와 크기가 그대로면 읽지 않는다(창으로 돌아올 때마다 부른다).
    /// </summary>
    public bool Refresh()
    {
        if (!Stale())
        {
            return false;
        }

        ScriptLibrary fresh = ScriptLibrary.Open(Root, Specs);
        bool changed = fresh.Files.Count != Library.Files.Count
            || fresh.Chapters.Count != Library.Chapters.Count
            || fresh.Files.Any(f => !_files.TryGetValue(f.Script.Path, out FileState? state) || state.Editor.Text != f.Script.Text)
            || !fresh.SourcePaths.SequenceEqual(Library.SourcePaths);

        Library = fresh;
        Adopt();

        if (changed)
        {
            Revision++;
        }

        return changed;
    }

    /// <summary>이 줄이 연 뒤로 고쳐졌는가 — 그 줄의 커맨드 묶음이 처음 읽은 것과 다르다.</summary>
    public bool IsEdited(LibraryLine line) => Original(line) is { } original && !Block(original.Text, original.Line).SequenceEqual(Block(line.File.Script.Text, line.Read));

    /// <summary>
    /// 이 줄을 처음 읽었을 때의 모습(그 파일의 글과 줄) — 고치기 전과 맞대어 볼 때 쓴다.
    /// 그때 이 줄이 없었으면(LineId가 없거나 바깥에서 새로 생겼다) null.
    /// </summary>
    public (string Text, LineRead Line)? Original(LibraryLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.Read.LineId is not null
            && _files.TryGetValue(line.File.Script.Path, out FileState? state)
            && state.OriginalRead.TryGet(line.Read.LineId, out LineRead original)
                ? (state.Original, original)
                : null;
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
                return false;
            }

            to.Add(path);
            Swap(state);
            return true;
        }

        return false;
    }

    /// <summary>그 파일의 지금 상태 — 디스크의 글이 우리가 마지막에 쓴 것과 다르면 디스크에서 다시 시작한다.</summary>
    private FileState Current(string path)
    {
        // 쓰기 앞에서는 언제나 글을 읽어 견준다 — 때와 크기가 같아도 글이 다를 수 있고, 틀리면 바깥의 고침을 덮어쓴다.
        ScriptFile disk = ScriptFile.Load(path);
        _stamps[path] = FileStamp.Of(path);

        if (_files.TryGetValue(path, out FileState? state) && state.Editor.Text == disk.Text)
        {
            return state;
        }

        // 바깥에서 바뀌었다 — 그 파일의 되돌리기를 비우고 디스크의 글을 읽은 대본으로 삼는다.
        Forget(path);
        state = new FileState(disk, Specs);
        _files[path] = state;
        Library = Library.WithFile(new LibraryFile(disk, state.OriginalRead));
        Revision++;
        return state;
    }

    private void Forget(string path)
    {
        _files.Remove(path);
        _undo.RemoveAll(p => p == path);
        _redo.RemoveAll(p => p == path);
    }

    private string? Save(FileState state)
    {
        try
        {
            state.Script.Save(state.Editor.Text);
            _stamps[state.Script.Path] = FileStamp.Of(state.Script.Path);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"파일에 쓰지 못했다 — {exception.Message}";
        }
    }

    // 고친 파일 하나만 다시 읽어 대본에 갈아 끼운다 — 폴더를 다시 읽지 않는다.
    private void Swap(FileState state)
    {
        string text = state.Editor.Text;
        Library = Library.WithFile(new LibraryFile(state.Script.WithText(text), YarnLineReader.ReadFile(text, state.Script.Path, Specs)));
        Revision++;
    }

    // 디스크의 파일이 우리가 아는 것과 달라졌는가 — 때 · 크기 · 파일의 집합.
    private bool Stale()
    {
        try
        {
            HashSet<string> known = Library.SourcePaths.ToHashSet(StringComparer.Ordinal);

            if (Directory.Exists(Root))
            {
                HashSet<string> disk = Directory.GetFiles(Root, "*.yarn", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(Root, "*.progression.json", SearchOption.AllDirectories))
                    .ToHashSet(StringComparer.Ordinal);

                // 읽지 못해 건너뛴 파일은 아는 집합에 없다 — 그 수가 달라졌을 때만 다시 읽는다.
                if (disk.Count != _stamps.Count || !disk.SetEquals(_stamps.Keys))
                {
                    return true;
                }
            }

            return known.Any(path => !_stamps.TryGetValue(path, out FileStamp stamp) || stamp != FileStamp.Of(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // 읽은 대본을 받아들인다 — 처음 보는 파일은 지금의 글이 "처음 읽은 것"이고,
    // 우리가 쓴 것과 다른 글이 디스크에 있으면 바깥에서 바뀐 것이다(그 파일의 되돌리기를 비운다).
    private void Adopt()
    {
        foreach (LibraryFile file in Library.Files)
        {
            string path = file.Script.Path;

            if (_files.TryGetValue(path, out FileState? state) && state.Editor.Text == file.Script.Text)
            {
                continue;
            }

            Forget(path);
            _files[path] = new FileState(file.Script, Specs, file.Read);
        }

        _stamps.Clear();

        IEnumerable<string> paths = Directory.Exists(Root)
            ? Directory.GetFiles(Root, "*.yarn", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Root, "*.progression.json", SearchOption.AllDirectories))
            : Library.SourcePaths;

        foreach (string path in paths)
        {
            _stamps[path] = FileStamp.Of(path);
        }
    }

    /// <summary>파일의 때와 크기 — 글을 읽지 않고 "바뀌었는가"를 가리는 표.</summary>
    private readonly record struct FileStamp(DateTime WrittenUtc, long Length)
    {
        public static FileStamp Of(string path)
        {
            FileInfo info = new(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : default;
        }
    }

    private sealed class FileState
    {
        public FileState(ScriptFile script, StageCommandSpecTable specs, YarnFileRead? read = null)
        {
            Script = script;
            Editor = new StagingEditor(script.Text, specs);
            Original = script.Text;
            OriginalRead = read ?? YarnLineReader.ReadFile(script.Text, script.Path, specs);
        }

        public ScriptFile Script { get; }

        public StagingEditor Editor { get; }

        /// <summary>이 파일을 처음 읽은(또는 바깥에서 바뀐 뒤 다시 읽은) 글 — 고친 줄을 가리는 기준.</summary>
        public string Original { get; }

        public YarnFileRead OriginalRead { get; }
    }
}
