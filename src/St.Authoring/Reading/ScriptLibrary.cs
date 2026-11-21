using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>낱말이 대사 글 · 화자 · LineId에 들었는가(대소문자를 가리지 않는다).</summary>
    public bool Matches(string word)
        => Read.Body.Contains(word, StringComparison.OrdinalIgnoreCase)
           || Read.Speaker.Contains(word, StringComparison.OrdinalIgnoreCase)
           || (Id?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>에피소드 하나 — yarn 노드 하나다. <see cref="Missing"/>이면 진행 파일에는 있는데 대본에 그 노드가 없다.</summary>
public sealed record StoryEpisode(string Id, string Title, string NodeName, IReadOnlyList<LibraryLine> Lines, bool Missing = false);

/// <summary>장면 하나 — 에피소드의 묶음. ked-story-director는 장면 하나를 yarn 파일 하나로 내보낸다.</summary>
public sealed record StoryScene(string Id, string Name, IReadOnlyList<StoryEpisode> Episodes)
{
    public int LineCount => Episodes.Sum(e => e.Lines.Count);

    /// <summary>
    /// 장면 안에서 줄을 찾는다 — 에피소드의 차례 그대로, 맞는 줄만 남긴 에피소드의 목록을 낸다.
    /// 낱말을 띄어 쓰면 모두 든 줄이고, <paramref name="withKinds"/>를 주면 그 종류의 커맨드가 하나라도 있는 줄만,
    /// <paramref name="episodeId"/>를 주면 그 에피소드만 본다. 거르는 것이 있으면 줄이 남지 않은 에피소드는 빠진다.
    /// </summary>
    public IReadOnlyList<StoryEpisode> Search(string? query, IReadOnlySet<CommandKind>? withKinds = null, string? episodeId = null)
    {
        string[] words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool narrowing = words.Length > 0 || withKinds is not null;
        List<StoryEpisode> found = [];

        foreach (StoryEpisode episode in Episodes)
        {
            if (episodeId is not null && episode.Id != episodeId)
            {
                continue;
            }

            List<LibraryLine> lines = episode.Lines
                .Where(line => words.All(line.Matches))
                .Where(line => withKinds is null || withKinds.Any(kind => line.Count(kind) > 0))
                .ToList();

            if (lines.Count > 0 || !narrowing)
            {
                found.Add(episode with { Lines = lines });
            }
        }

        return found;
    }
}

/// <summary>챕터 하나 — 진행 파일(<c>*.progression.json</c>) 하나다.</summary>
public sealed record StoryChapter(string Id, string Name, IReadOnlyList<StoryScene> Scenes);

/// <summary>
/// 열어 둔 대본 — <b>챕터 → 장면 → 에피소드 → 줄</b>(docs/PLAN.md D8 · §4.7).
///
/// 챕터와 에피소드의 이름 · 차례는 대본 곁의 진행 파일(<c>*.progression.json</c> — ked-progression의 챕터)에서 읽는다:
/// 노드의 <c>SceneId</c>가 장면을, <c>DialogueEntryId</c>가 yarn 노드(<c>title:</c>)를 가리킨다.
/// 진행 파일에 없는 yarn 노드는 맨 뒤의 챕터 하나에 모은다 — 파일이 장면, 노드가 에피소드다.
/// 읽기만 한다 — 고치는 길은 <see cref="LineEditor"/>다.
/// </summary>
public sealed partial class ScriptLibrary
{
    /// <summary>진행 파일에 없는 대본을 모은 챕터의 Id.</summary>
    public const string LooseChapterId = "";

    private const string ProgressionPattern = "*.progression.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyList<ChapterSource> _sources;
    private readonly IReadOnlyList<string> _loadProblems;

    private ScriptLibrary(
        string root,
        IReadOnlyList<LibraryFile> files,
        IReadOnlyList<ChapterSource> sources,
        IReadOnlyList<string> loadProblems,
        IReadOnlyList<StoryChapter> chapters,
        IReadOnlyList<string> problems)
    {
        Root = root;
        Files = files;
        _sources = sources;
        _loadProblems = loadProblems;
        Chapters = chapters;
        Problems = problems;
    }

    public string Root { get; }

    public IReadOnlyList<LibraryFile> Files { get; }

    /// <summary>챕터 — 진행 파일의 이름 순. 진행 파일에 없는 대본이 있으면 맨 뒤에 한 챕터로 선다.</summary>
    public IReadOnlyList<StoryChapter> Chapters { get; }

    /// <summary>읽지 못한 파일 · 겹친 노드 이름.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>대사 줄의 수 — 모든 파일.</summary>
    public int LineCount => Files.Sum(f => f.Read.Lines.Count);

    /// <summary>이 대본을 이루는 파일(대본 · 진행 파일)의 경로 — 바깥에서 바뀌었는지 볼 때 쓴다.</summary>
    public IEnumerable<string> SourcePaths => Files.Select(f => f.Script.Path).Concat(_sources.Select(s => s.Path));

    [GeneratedRegex(@"^//\s*장면:\s*(?<name>.+?)\s*\((?<id>[^()\s]+)\)\s*$", RegexOptions.Multiline)]
    private static partial Regex SceneComment();

    /// <summary>파일이나 폴더를 연다. 읽지 못한 파일은 건너뛰고 <see cref="Problems"/>에 적는다.</summary>
    public static ScriptLibrary Open(string path, StageCommandSpecTable? specs = null)
    {
        ScriptSet set = ScriptSet.Open(path);
        List<LibraryFile> files = [];
        List<ChapterSource> sources = [];
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

        if (Directory.Exists(path))
        {
            string[] progressions = Directory.GetFiles(path, ProgressionPattern, SearchOption.AllDirectories);
            Array.Sort(progressions, StringComparer.Ordinal);

            foreach (string progression in progressions)
            {
                try
                {
                    if (JsonSerializer.Deserialize<ChapterDto>(File.ReadAllText(progression), Json) is { Nodes.Count: > 0 } dto)
                    {
                        sources.Add(new ChapterSource(progression, dto));
                    }
                }
                catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{progression} — 진행 파일을 읽지 못했다: {exception.Message}");
                }
            }
        }

        return Assemble(set.Root, files, sources, problems);
    }

    /// <summary>
    /// 파일 하나만 바뀐 대본 — 디스크를 다시 읽지 않고 그 파일의 줄만 갈아 끼워 챕터를 다시 짓는다.
    /// 고칠 때마다 폴더 전체를 다시 읽지 않게 한다. 이 대본에 없는 파일이면 그대로 돌려준다.
    /// </summary>
    public ScriptLibrary WithFile(LibraryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return Files.Any(f => f.Script.Path == file.Script.Path)
            ? Assemble(Root, Files.Select(f => f.Script.Path == file.Script.Path ? file : f).ToList(), _sources, _loadProblems)
            : this;
    }

    // 읽어 둔 파일과 진행 파일에서 챕터 · 장면 · 에피소드를 짓는다 — 디스크를 보지 않는다.
    private static ScriptLibrary Assemble(
        string root, IReadOnlyList<LibraryFile> files, IReadOnlyList<ChapterSource> sources, IReadOnlyList<string> loadProblems)
    {
        List<string> problems = [.. loadProblems];

        // yarn 노드 → 그 노드의 줄. 노드 이름은 Yarn 프로젝트에서 유일하다.
        Dictionary<string, Node> nodes = new(StringComparer.Ordinal);
        List<Node> order = [];

        foreach (LibraryFile file in files)
        {
            foreach (IGrouping<string, LineRead> group in file.Read.Lines.GroupBy(l => l.NodeName ?? string.Empty))
            {
                Node node = new(group.Key, file, group.Select(l => new LibraryLine(file, l)).ToList());

                if (!nodes.TryAdd(node.Name, node))
                {
                    problems.Add($"노드 '{node.Name}'이 {nodes[node.Name].File.Script.Path}와 {file.Script.Path}에 있다 — 앞의 것으로 읽는다");
                    continue;
                }

                order.Add(node);
            }
        }

        HashSet<string> used = new(StringComparer.Ordinal);
        List<StoryChapter> chapters = sources.Select(source => BuildChapter(source, nodes, used)).ToList();

        List<StoryScene> loose = order
            .Where(node => !used.Contains(node.Name))
            .GroupBy(node => node.File)
            .Select(group => new StoryScene(
                group.Key.Script.Path,
                SceneName(group.Key, null) ?? Path.GetFileNameWithoutExtension(group.Key.Script.Path),
                group.Select(node => new StoryEpisode(node.Name, node.Name, node.Name, node.Lines)).ToList()))
            .ToList();

        if (loose.Count > 0)
        {
            chapters.Add(new StoryChapter(LooseChapterId, chapters.Count == 0 ? "대본" : "진행 파일에 없는 대본", loose));
        }

        return new ScriptLibrary(root, files, sources, loadProblems, chapters, problems);
    }

    private static StoryChapter BuildChapter(ChapterSource source, Dictionary<string, Node> nodes, HashSet<string> used)
    {
        ChapterDto dto = source.Dto;

        // 장면은 에피소드가 처음 나오는 차례로 선다 — 진행 파일에는 장면의 차례가 따로 없다.
        List<string> sceneIds = [];
        Dictionary<string, List<StoryEpisode>> episodes = new(StringComparer.Ordinal);
        Dictionary<string, LibraryFile> sceneFiles = new(StringComparer.Ordinal);

        foreach (EpisodeDto episode in dto.Nodes ?? [])
        {
            string id = episode.EpisodeId ?? string.Empty;
            string entry = string.IsNullOrEmpty(episode.DialogueEntryId) ? id : episode.DialogueEntryId;
            string sceneId = string.IsNullOrEmpty(episode.SceneId) ? id : episode.SceneId;
            string title = string.IsNullOrEmpty(episode.Title) ? id : episode.Title;

            if (!episodes.TryGetValue(sceneId, out List<StoryEpisode>? list))
            {
                episodes.Add(sceneId, list = []);
                sceneIds.Add(sceneId);
            }

            if (nodes.TryGetValue(entry, out Node? node))
            {
                used.Add(entry);
                sceneFiles.TryAdd(sceneId, node.File);
                list.Add(new StoryEpisode(id, title, entry, node.Lines));
            }
            else
            {
                list.Add(new StoryEpisode(id, title, entry, [], Missing: true));
            }
        }

        List<StoryScene> scenes = sceneIds
            .Select(id => new StoryScene(
                id, sceneFiles.TryGetValue(id, out LibraryFile? file) ? SceneName(file, id) ?? id : id, episodes[id]))
            .ToList();

        string chapterId = string.IsNullOrEmpty(dto.ChapterId) ? Path.GetFileName(source.Path) : dto.ChapterId;
        return new StoryChapter(chapterId, string.IsNullOrEmpty(dto.DisplayName) ? chapterId : dto.DisplayName, scenes);
    }

    /// <summary>
    /// 장면의 보이는 이름 — ked-story-director가 파일 머리에 적는 주석 <c>// 장면: 이름 (id)</c>.
    /// <paramref name="sceneId"/>를 주면 그 id의 것만 받는다. 없으면 null.
    /// </summary>
    private static string? SceneName(LibraryFile file, string? sceneId)
    {
        foreach (Match match in SceneComment().Matches(file.Script.Text.Replace("\r\n", "\n")))
        {
            if (sceneId is null || match.Groups["id"].Value == sceneId)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    private sealed record Node(string Name, LibraryFile File, IReadOnlyList<LibraryLine> Lines);

    /// <summary>읽어 둔 진행 파일 하나 — 챕터를 다시 지을 때 디스크를 다시 보지 않는다.</summary>
    private sealed record ChapterSource(string Path, ChapterDto Dto);

    // 진행 파일(ked-progression의 챕터)에서 이 툴이 읽는 칸만.
    private sealed record ChapterDto(string? ChapterId, string? DisplayName, List<EpisodeDto>? Nodes);

    private sealed record EpisodeDto(string? EpisodeId, string? Title, string? DialogueEntryId, string? SceneId);
}
