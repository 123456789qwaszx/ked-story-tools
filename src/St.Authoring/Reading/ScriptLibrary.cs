using Ked.Presentation.Core;
using St.Authoring.Editing;
using St.Authoring.Scripts;

namespace St.Authoring.Reading;

/// <summary>열어 둔 대본 파일 하나 — 글과 줄 읽기의 결과.</summary>
public sealed record LibraryFile(ScriptFile Script, YarnFileRead Read);

/// <summary>대본의 대사 줄 하나 — 어느 파일의 것인가와 함께.</summary>
public sealed record LibraryLine(LibraryFile File, LineRead Read)
{
    /// <summary><c>#line:</c> 뒤의 글. 태그가 없으면 null.</summary>
    public string? Id => Read.LineId is null ? null : CommandAddress.ShortLineId(Read.LineId);

    /// <summary>그 종류의 커맨드 수.</summary>
    public int Count(CommandKind kind) => Read.Steps.Sum(s => s.Commands.Count(c => c.Spec?.Kind == kind));

    public bool Editable => LineEditor.IsEditable(Read, out _);
}

/// <summary>
/// 열어 둔 대본 — 파일 하나이거나 폴더 아래의 <c>*.yarn</c> 전부(Q2). 줄을 대사 글 · 화자 · LineId로 찾는다.
/// 읽기만 한다 — 고치는 길은 <see cref="LineEditor"/>다.
/// </summary>
public sealed class ScriptLibrary
{
    private ScriptLibrary(string root, IReadOnlyList<LibraryFile> files, IReadOnlyList<string> problems)
    {
        Root = root;
        Files = files;
        Problems = problems;
        Lines = files.SelectMany(f => f.Read.Lines.Select(l => new LibraryLine(f, l))).ToList();
    }

    public string Root { get; }

    public IReadOnlyList<LibraryFile> Files { get; }

    /// <summary>모든 대사 줄 — 파일 이름 순, 파일 안에서는 쓴 차례.</summary>
    public IReadOnlyList<LibraryLine> Lines { get; }

    /// <summary>읽지 못한 파일(UTF-8이 아니다 …).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>파일이나 폴더를 연다. 읽지 못한 파일은 건너뛰고 <see cref="Problems"/>에 적는다.</summary>
    public static ScriptLibrary Open(string path, StageCommandSpecTable? specs = null)
    {
        ScriptSet set = ScriptSet.Open(path);
        List<LibraryFile> files = [];
        List<string> problems = [];

        foreach (string file in set.Paths)
        {
            try
            {
                ScriptFile script = ScriptFile.Load(file);
                files.Add(new LibraryFile(script, YarnLineReader.ReadFile(script.Text, file, specs)));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add(exception.Message);
            }
        }

        return new ScriptLibrary(set.Root, files, problems);
    }

    /// <summary>
    /// 줄을 찾는다 — 낱말을 띄어 쓰면 모두 든 줄이다(대사 글 · 화자 · LineId, 대소문자를 가리지 않는다).
    /// <paramref name="withKinds"/>를 주면 그 종류의 커맨드가 하나라도 있는 줄만 남긴다.
    /// </summary>
    public IReadOnlyList<LibraryLine> Search(string? query, IReadOnlySet<CommandKind>? withKinds = null)
    {
        string[] words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return Lines
            .Where(line => words.All(word => Matches(line, word)))
            .Where(line => withKinds is null || withKinds.Any(kind => line.Count(kind) > 0))
            .ToList();
    }

    private static bool Matches(LibraryLine line, string word)
        => line.Read.Body.Contains(word, StringComparison.OrdinalIgnoreCase)
           || line.Read.Speaker.Contains(word, StringComparison.OrdinalIgnoreCase)
           || (line.Id?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false);
}
