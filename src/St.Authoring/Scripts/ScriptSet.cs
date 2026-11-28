using Ked.Presentation.Core;
using St.Authoring.Composing;
using St.Authoring.Editing;

namespace St.Authoring.Scripts;

/// <summary>
/// 툴이 여는 단위 — yarn 파일 하나이거나 대본 폴더(그 아래의 <c>*.yarn</c> 전부)다.
/// LineId는 프로젝트에서 유일하므로 폴더를 열면 LineId만으로 파일을 찾는다.
/// </summary>
public sealed class ScriptSet
{
    private ScriptSet(string root, IReadOnlyList<string> paths)
    {
        Root = root;
        Paths = paths;
    }

    /// <summary>연 경로 — 파일이나 폴더.</summary>
    public string Root { get; }

    /// <summary>yarn 파일의 경로 — 이름 순.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>파일이나 폴더를 연다. 없으면 <see cref="FileNotFoundException"/>.</summary>
    public static ScriptSet Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(path))
        {
            return new ScriptSet(path, [path]);
        }

        if (Directory.Exists(path))
        {
            // 보관소(*.retired.yarn — docs/PLAN.md §2.5 C3)는 대본이 아니다. 대사 층에서 사라진 줄의 연출이 든 곳이다.
            string[] found = Directory.GetFiles(path, "*.yarn", SearchOption.AllDirectories)
                .Where(file => !LayerComposer.IsRetiredName(Path.GetFileName(file)))
                .ToArray();
            Array.Sort(found, StringComparer.Ordinal);
            return new ScriptSet(path, found);
        }

        throw new FileNotFoundException($"{path} — 그런 파일도 폴더도 없다", path);
    }

    /// <summary>
    /// 그 LineId가 든 파일을 찾는다. 없거나 둘 이상의 파일에 있으면 <see cref="InvalidDataException"/> —
    /// 어느 줄인지 가를 수 없으면 고치지 않는다.
    /// </summary>
    public ScriptFile Find(string lineId, StageCommandSpecTable? specs = null)
    {
        string id = YarnLineReader.NormalizeLineId(lineId);
        List<ScriptFile> hits = [];

        foreach (string path in Paths)
        {
            ScriptFile file = ScriptFile.Load(path);

            if (YarnLineReader.ReadFile(file.Text, path, specs).TryGet(id, out _))
            {
                hits.Add(file);
            }
        }

        return hits.Count switch
        {
            1 => hits[0],
            0 => throw new InvalidDataException($"LineId '{CommandAddress.ShortLineId(id)}'가 {Root}에 없다"),
            _ => throw new InvalidDataException(
                $"LineId '{CommandAddress.ShortLineId(id)}'가 파일 {hits.Count}개에 있다 — {string.Join(" · ", hits.Select(h => h.Path))}"),
        };
    }
}
