using Ked.Presentation.Core;
using System.Text.Json.Nodes;
using St.Authoring.Composing;
using St.Authoring.Motions;
using St.Authoring.Reading;
using St.Authoring.Scripts;

namespace St.Authoring.Editing;

/// <summary>
/// 열어 둔 대본을 고치는 자리 — 화면이 쥔다(docs/PLAN.md U1).
///
///   조작마다 바로 파일에 쓴다 — Unity에서 되감아 바로 보는 것이 고치는 고리다. 되돌리기 · 다시 하기도 파일에 쓴다.
///   되돌리기는 파일을 넘나드는 한 줄이다 — 조작 하나(또는 한 덩이)가 한 칸.
///   줄의 모션을 풀거나 솎으면 그 편의 모션 파일도 함께 쓴다 — 대본의 조작과 한 칸으로 되돌린다.
///   바깥에서 파일이 바뀌었으면 그 파일의 되돌리기를 비우고 디스크의 글에서 다시 시작한다 — 바깥의 고침을 덮어쓰지 않는다.
///
///   연 폴더가 연출 층이면(<c>compose.json</c>이 있다 — docs/PLAN.md §2.5 C5) 열 때 · 고칠 때마다 · 다시 볼 때 대사 층이 바뀌었으면
///   합친다(<see cref="ComposeFolder"/>). 게임 yarn은 그 결과물이다 — 되돌리면 다시 합쳐 따라온다. 합치기가 들인 대사 층의 변화는
///   바깥의 고침과 같다 — 되돌리기 칸이 아니고, 그 파일의 되돌리기를 비운다.
///
/// 고치는 길은 <see cref="LineEditor"/> 하나다 — 여기는 파일과 되돌리기의 살림만 한다.
/// 고칠 때마다 폴더를 다시 읽지 않는다: 바뀐 파일 하나만 다시 읽어 갈아 끼우고(<see cref="ScriptLibrary.WithFile"/>),
/// 바깥의 고침은 파일의 때와 크기로 먼저 가린다.
/// </summary>
public sealed class EditSession
{
    private readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileStamp> _stamps = new(StringComparer.Ordinal);
    private readonly List<UndoStep> _undo = [];
    private readonly List<UndoStep> _redo = [];
    private readonly Dictionary<string, FileStamp> _dialogue = new(StringComparer.Ordinal);

    private EditSession(string root, StageCommandSpecTable? specs, ComposeSettings? layers)
    {
        Root = root;
        Specs = specs ?? StageCommandSpecTable.Default;
        Layers = layers;

        // 먼저 합친다 — 대사 층의 새 줄 · 지운 줄이 연출 층에 들어온 뒤에 읽는다.
        Compose();
        Library = Read();
        Adopt();
    }

    /// <summary>연 폴더가 연출 층이면 그 짝(대사 층 · 게임 폴더). 층을 두지 않은 대본이면 null.</summary>
    public ComposeSettings? Layers { get; }

    /// <summary>마지막 합치기의 결과 — 줄마다의 알림과 쓴 파일. 층이 없거나 합치기가 돌지 못했으면 null.</summary>
    public ComposeOutcome? Composed { get; private set; }

    /// <summary>마지막 합치기가 게임에 들이지 못한 까닭 — 거절 · 멈춘 파일 · 없는 폴더. 다 들어갔으면 null.</summary>
    public string? ComposeProblem { get; private set; }

    // 진행 파일이 있는 곳 — 대사 층(exported/yarn)의 위.
    private string? ProgressionFolder => Layers is null ? null : Path.GetDirectoryName(Layers.DialogueFolder(Root));

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
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return new EditSession(path, specs, Directory.Exists(path) ? ComposeSettings.Load(path) : null);
        }
        catch (InvalidDataException exception)
        {
            // 깨진 compose.json — 여는 쪽은 읽지 못한 폴더로 다룬다.
            throw new IOException(exception.Message, exception);
        }
    }

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

        _undo.Add(new UndoStep(path, null));
        _redo.Clear();
        Swap(state);
        Recompose();
        return outcome;
    }

    // ── 줄의 모션 (docs/PLAN.md §4.8) ───────────────────────────────────────

    /// <summary>
    /// 그 모션 커맨드가 트는 모션을 **이 줄의 것으로 푼다** — 모션을 그 편의 모션 파일에 새 이름으로 복사하고,
    /// 대본의 그 커맨드가 새 이름을 가리키게 한다. 원래의 모션과 그것을 쓰는 다른 줄은 그대로다. 되돌리기 한 칸이다.
    /// </summary>
    public EditOutcome UnpackMotion(string path, CommandAddress target)
        => ChangeMotion(path, target, (state, command, name) =>
        {
            string episode = MotionFile.EpisodePath(path);
            string? before = ReadOrNull(episode);
            JsonObject? source = (before is null ? null : MotionFile.Find(before, name))
                ?? (ReadOrNull(MotionFile.SharedPath(path)) is { } shared ? MotionFile.Find(shared, name) : null);

            if (source is null)
            {
                return (null, null, $"모션 '{name}'을 모션 파일에서 찾지 못했다");
            }

            string fresh = FreshName(name, target.LineId, before is null ? [] : MotionFile.Names(before));
            string after = MotionFile.Add(before, MotionFile.Renamed(source, fresh));
            return (new MotionChange(episode, before, after), new SetValue(target, MotionLibrary.Named(command)!.Value.Spec.Name, fresh), null);
        });

    /// <summary>
    /// 그 모션 커맨드가 트는 모션의 **키를 솎는다** — 프레임마다 찍힌 키를, 눈에 띄지 않는 한도 안에서 적은 수로 줄인다.
    /// 그 편의 모션 파일에 든 모션만 솎는다(모두가 쓰는 파일의 것은 먼저 푼다). 대본은 바뀌지 않는다.
    /// </summary>
    public EditOutcome SimplifyMotion(string path, CommandAddress target)
        => ChangeMotion(path, target, (state, command, name) =>
        {
            string episode = MotionFile.EpisodePath(path);

            if (ReadOrNull(episode) is not { } before || MotionFile.Find(before, name) is not { } motion)
            {
                return (null, null, $"모션 '{name}'은 이 편의 모션 파일에 없다 — 먼저 이 줄의 것으로 푼다");
            }

            (JsonObject simplified, int had, int has) = MotionFile.Simplified(motion);
            return has == had ? (null, null, null) : (new MotionChange(episode, before, MotionFile.Replace(before, name, simplified)), null, null);
        });

    /// <summary>
    /// 그 편의 모션 파일에 든 모션 하나를 갈아 쓴다 — 그래프에서 키를 고친 것(docs/PLAN.md §4.8). 되돌리기 한 칸이다.
    /// 대본은 바뀌지 않는다. 쓰지 못했으면 까닭을, 썼거나 바뀐 것이 없으면 null을 돌려준다.
    /// </summary>
    public string? WriteMotion(string path, string name, JsonObject motion)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(motion);

        try
        {
            string episode = MotionFile.EpisodePath(path);

            if (ReadOrNull(episode) is not { } before)
            {
                return "이 편의 모션 파일이 없다";
            }

            string after = MotionFile.Replace(before, name, motion);

            if (after == before)
            {
                return null;
            }

            File.WriteAllText(episode, after);
            _undo.Add(new UndoStep(null, new MotionChange(episode, before, after)));
            _redo.Clear();
            Revision++;
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return $"모션 파일을 고치지 못했다 — {exception.Message}";
        }
    }

    private EditOutcome ChangeMotion(
        string path, CommandAddress target, Func<FileState, LineReadCommand, string, (MotionChange? Change, LineEdit? Edit, string? Error)> plan)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            FileState state = Current(path);
            YarnFileRead read = YarnLineReader.ReadFile(state.Editor.Text, path, Specs);

            if (!read.TryGet(target.LineId, out LineRead line)
                || line.Steps.SelectMany(s => s.Commands).FirstOrDefault(c => CommandAddress.Of(line, c) == target) is not { } command
                || MotionLibrary.NameOf(command) is not { } name)
            {
                return EditOutcome.Refused(state.Editor.Text, "모션 커맨드가 아니다");
            }

            (MotionChange? change, LineEdit? edit, string? error) = plan(state, command, name);

            if (error is not null)
            {
                return EditOutcome.Refused(state.Editor.Text, error);
            }

            if (change is null)
            {
                return EditOutcome.Unchanged(state.Editor.Text, line, target);
            }

            // 모션 파일을 먼저 쓴다 — 대본이 없는 이름을 가리키는 때가 없게.
            File.WriteAllText(change.Path, change.After);

            if (edit is null)
            {
                _undo.Add(new UndoStep(null, change));
                _redo.Clear();
                Revision++;
                return EditOutcome.MotionOnly(state.Editor.Text, line, target);
            }

            EditOutcome outcome = state.Editor.Apply([edit]);

            if (outcome is not { Ok: true, Changed: true })
            {
                Restore(change);
                return outcome.Ok ? EditOutcome.Refused(state.Editor.Text, "대본의 이름이 바뀌지 않았다") : outcome;
            }

            if (Save(state) is { } failure)
            {
                state.Editor.Undo();
                Restore(change);
                return EditOutcome.Refused(state.Editor.Text, failure);
            }

            _undo.Add(new UndoStep(path, change));
            _redo.Clear();
            Swap(state);
            Recompose();
            return outcome;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return EditOutcome.Refused(string.Empty, $"모션 파일을 고치지 못했다 — {exception.Message}");
        }
    }

    // 줄의 모션의 이름 — 원래 이름에 줄의 ID를 붙인다. 런타임의 이름 규칙([a-z0-9_]+)을 지킨다.
    private static string FreshName(string name, string lineId, IReadOnlyList<string> taken)
    {
        string key = new(lineId[(lineId.IndexOf(':') + 1)..].ToLowerInvariant().Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' ? c : '_').ToArray());
        string fresh = $"{name}_{key}";

        for (int i = 2; taken.Contains(fresh); i++)
        {
            fresh = $"{name}_{key}_{i}";
        }

        return fresh;
    }

    private static string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    // 모션 파일을 고치기 전으로 — 우리가 새로 만든 파일이면 지운다.
    private static void Restore(MotionChange change)
    {
        if (change.Before is null)
        {
            File.Delete(change.Path);
        }
        else
        {
            File.WriteAllText(change.Path, change.Before);
        }
    }

    /// <summary>마지막 조작을 되돌려 파일에 쓴다. 되돌릴 것이 없으면 false.</summary>
    public bool Undo() => Step(_undo, _redo, undo: true);

    /// <summary>되돌린 조작을 다시 해 파일에 쓴다.</summary>
    public bool Redo() => Step(_redo, _undo, undo: false);

    /// <summary>
    /// 디스크를 다시 본다 — 바깥에서 바뀐 것이 있으면 다시 읽고, 바뀐 파일의 되돌리기를 비운다. 바뀐 것이 있었으면 true.
    /// 파일의 때와 크기가 그대로면 읽지 않는다(창으로 돌아올 때마다 부른다).
    /// </summary>
    public bool Refresh()
    {
        // 대사 층이 바뀌었으면 먼저 들인다(C5) — 무엇이 바뀌었는지는 Composed에 있다.
        bool merged = DialogueStale();

        if (merged)
        {
            Compose();
        }

        if (!Stale())
        {
            if (merged)
            {
                Revision++;
            }

            return merged;
        }

        ScriptLibrary fresh = Read();
        bool changed = fresh.Files.Count != Library.Files.Count
            || fresh.Chapters.Count != Library.Chapters.Count
            || fresh.Files.Any(f => !_files.TryGetValue(f.Script.Path, out FileState? state) || state.Editor.Text != f.Script.Text)
            || !fresh.SourcePaths.SequenceEqual(Library.SourcePaths);

        Library = fresh;
        Adopt();

        if (changed || merged)
        {
            Revision++;
        }

        return changed || merged;
    }

    // ── 층 (docs/PLAN.md §2.5) ──────────────────────────────────────────────

    private ScriptLibrary Read() => ScriptLibrary.Open(Root, Specs, ProgressionFolder);

    /// <summary>합친다 — 층이 없으면 아무것도 하지 않는다. 합치기가 연출 층의 파일을 고쳤으면(대사 층이 바뀌었다) true.</summary>
    private bool Compose()
    {
        if (Layers is null)
        {
            return false;
        }

        try
        {
            Composed = ComposeFolder.Run(Root, Layers, specs: Specs);
            ComposeProblem = !Composed.Result.Ok
                ? $"합치지 못했다 — {string.Join(" · ", Composed.Result.Errors)}"
                : Composed.Files.FirstOrDefault(f => f.Change == FileChange.Held) is { } held
                    ? $"게임 yarn {held.Name}에 쓰지 않았다 — {held.Reason}. 대사는 디렉터에서, 연출은 이 툴에서 고친다"
                    : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Composed = null;
            ComposeProblem = $"합치지 못했다 — {exception.Message}";
        }

        _dialogue.Clear();

        foreach (string path in DialoguePaths())
        {
            _dialogue[path] = FileStamp.Of(path);
        }

        return Composed?.Files.Any(f => f.Layer != ComposeFolder.GameLayer && f.Change != FileChange.Held) == true;
    }

    // 연출 층을 고친 뒤 — 게임 yarn이 따라온다. 그 김에 대사 층의 변화가 들어왔으면 바깥의 고침처럼 다시 읽는다.
    private void Recompose()
    {
        if (Compose())
        {
            Library = Read();
            Adopt();
            Revision++;
        }
    }

    private IEnumerable<string> DialoguePaths()
    {
        if (Layers is null)
        {
            return [];
        }

        try
        {
            string folder = Layers.DialogueFolder(Root);
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.yarn") : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // 대사 층이 마지막으로 합친 뒤에 바뀌었는가 — 때 · 크기 · 파일의 집합.
    private bool DialogueStale()
    {
        if (Layers is null)
        {
            return false;
        }

        List<string> paths = DialoguePaths().ToList();
        return paths.Count != _dialogue.Count || paths.Any(p => !_dialogue.TryGetValue(p, out FileStamp stamp) || stamp != FileStamp.Of(p));
    }

    // 이 대본을 이루는 디스크의 파일 — 대본과 진행 파일. 읽는 쪽(ScriptLibrary)과 같은 것을 센다.
    private IEnumerable<string> DiskPaths()
        => Directory.Exists(Root)
            ? ScriptSet.Open(Root).Paths.Concat(ScriptLibrary.ProgressionPaths(Root, ProgressionFolder))
            : Library.SourcePaths;

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

    private bool Step(List<UndoStep> from, List<UndoStep> to, bool undo)
    {
        while (from.Count > 0)
        {
            UndoStep step = from[^1];
            from.RemoveAt(from.Count - 1);

            // 모션 파일이 바깥에서 바뀌었으면(우리가 쓴 글이 아니다) 그 칸은 건너뛴다 — 덮어쓰지 않는다.
            if (step.Motion is { } motion)
            {
                try
                {
                    if (ReadOrNull(motion.Path) != (undo ? motion.After : motion.Before))
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }

            FileState? state = null;

            if (step.Yarn is { } path)
            {
                try
                {
                    state = Current(path);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    continue;
                }

                // 바깥에서 바뀐 파일은 Current가 되돌리기를 비웠다 — 그 칸은 건너뛴다.
                if (!(undo ? state.Editor.Undo() : state.Editor.Redo()))
                {
                    continue;
                }

                if (Save(state) is not null)
                {
                    Forget(path);
                    return false;
                }
            }

            try
            {
                if (step.Motion is { } change)
                {
                    if (undo)
                    {
                        Restore(change);
                    }
                    else
                    {
                        File.WriteAllText(change.Path, change.After);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            to.Add(step);

            if (state is not null)
            {
                Swap(state);
                Recompose();
            }
            else
            {
                Revision++;
            }

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
        _undo.RemoveAll(step => step.Yarn == path);
        _redo.RemoveAll(step => step.Yarn == path);
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
                HashSet<string> disk = DiskPaths().ToHashSet(StringComparer.Ordinal);

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

        foreach (string path in DiskPaths().ToList())
        {
            _stamps[path] = FileStamp.Of(path);
        }
    }

    /// <summary>되돌리기 한 칸 — 대본의 조작 하나(한 덩이), 모션 파일의 고침 하나, 또는 둘이 함께.</summary>
    private sealed record UndoStep(string? Yarn, MotionChange? Motion);

    /// <summary>모션 파일의 고침 — 앞의 글이 null이면 그때 파일이 없었다.</summary>
    private sealed record MotionChange(string Path, string? Before, string After);

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
