using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ked.Presentation.Core;
using St.Authoring.Scripts;

namespace St.Authoring.Composing;

/// <summary>
/// 연출 층 폴더의 짝(C8 — docs/PLAN.md §2.5) — <c>staging/compose.json</c>. 대사 층과 게임 폴더를 연출 층 폴더에서 본 상대 경로로 적는다.
/// 저장소에 함께 커밋되어 화면 · <c>st</c> · 다른 컴퓨터가 같은 짝을 쓴다.
/// </summary>
/// <param name="Dialogue">대사 층 — 디렉터의 <c>exported/yarn</c>.</param>
/// <param name="Out">게임 폴더 — 합친 yarn을 쓸 곳(런타임의 스토리 폴더).</param>
public sealed record ComposeSettings(string Dialogue, string Out)
{
    public const string FileName = "compose.json";

    /// <summary>적지 않았을 때의 대사 층 — 이야기 폴더의 <c>exported/yarn</c>.</summary>
    public const string DefaultDialogue = "../exported/yarn";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>그 연출 층 폴더의 설정. 파일이 없으면 null, 읽지 못하거나 게임 폴더가 비었으면 <see cref="InvalidDataException"/>.</summary>
    public static ComposeSettings? Load(string stagingFolder)
    {
        ArgumentNullException.ThrowIfNull(stagingFolder);
        string path = Path.Combine(stagingFolder, FileName);

        if (!File.Exists(path))
        {
            return null;
        }

        Dto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} — 읽지 못했다: {exception.Message}");
        }

        if (dto is not { Out: { } game } || string.IsNullOrWhiteSpace(game))
        {
            throw new InvalidDataException($"{path} — 게임 폴더(out)가 없다");
        }

        return new ComposeSettings(dto.Dialogue is { } dialogue && !string.IsNullOrWhiteSpace(dialogue) ? dialogue : DefaultDialogue, game);
    }

    public void Save(string stagingFolder)
    {
        ArgumentNullException.ThrowIfNull(stagingFolder);
        File.WriteAllText(Path.Combine(stagingFolder, FileName), JsonSerializer.Serialize(new Dto { Dialogue = Dialogue, Out = Out }, Json) + "\n");
    }

    /// <summary>어느 폴더를 연출 층 폴더에서 본 상대 경로로 — 설정에 적는 꼴(빗금은 <c>/</c>).</summary>
    public static string Relative(string stagingFolder, string folder)
        => Path.GetRelativePath(Path.GetFullPath(stagingFolder), Path.GetFullPath(folder)).Replace('\\', '/');

    public string DialogueFolder(string stagingFolder) => Path.GetFullPath(Path.Combine(stagingFolder, Dialogue));

    public string GameFolder(string stagingFolder) => Path.GetFullPath(Path.Combine(stagingFolder, Out));

    private sealed class Dto
    {
        public string? Dialogue { get; set; }

        public string? Out { get; set; }
    }
}

/// <summary>합치기가 파일 하나에 한 일.</summary>
public enum FileChange
{
    Written,
    Removed,

    /// <summary>쓰지 않고 멈췄다 — 손으로 고친 게임 yarn(C6).</summary>
    Held,
}

/// <param name="Layer">어느 묶음인가 — <c>게임</c> · <c>연출 층</c> · <c>보관소</c>.</param>
public sealed record ComposeFileNote(string Layer, string Name, FileChange Change, string? Reason = null);

/// <summary>폴더에서 돌린 합치기의 결과.</summary>
/// <param name="Result">합친 글과 줄마다의 알림. 거절이면 <see cref="ComposeResult.Errors"/>에 까닭이 있고 아무것도 쓰지 않았다.</param>
/// <param name="Files">쓰고 · 걷고 · 멈춘 파일. 바이트가 같아 그대로 둔 것은 들지 않는다.</param>
/// <param name="Scenes">대사 층의 장면(파일) 수.</param>
/// <param name="Lines">대사 층의 줄(줄 ID) 수.</param>
/// <param name="Unchanged">바이트가 같아 그대로 둔 파일 수.</param>
/// <param name="DryRun">쓰지 않고 셈만 했다.</param>
public sealed record ComposeOutcome(
    ComposeResult Result, IReadOnlyList<ComposeFileNote> Files, int Scenes, int Lines, int Unchanged, bool DryRun)
{
    /// <summary>대사 층의 줄 — 줄 ID(<c>line:abc</c>) → 그 물리 줄. 화면이 연출 층의 줄과 견주어 딱지를 단다(C7).</summary>
    public IReadOnlyDictionary<string, string> DialogueLines { get; init; } = new Dictionary<string, string>();

    /// <summary>멈춘 파일이 있다 — 그 장면은 게임에 들어가지 않았다.</summary>
    public bool Held => Files.Any(f => f.Change == FileChange.Held);
}

/// <summary>
/// 연출 층 폴더에서 합치기를 돌린다(K8 I2 — docs/PLAN.md §2.5). 디스크를 읽어 <see cref="LayerComposer"/>에 넘기고 결과를 쓴다 —
/// 보관소 · 연출 층 · 게임 yarn의 차례로, 바이트가 다른 파일만.
///
/// <b>손으로 고친 게임 yarn은 덮지 않는다(C6).</b> 마지막으로 쓴 게임 yarn의 지문을 <c>staging/.composed.json</c>에 적어 두고,
/// 게임 폴더의 파일이 그 지문과 다르면(기록에 없어도) 그 파일만 멈춘다. 다른 장면은 그대로 쓴다. <c>force</c>면 덮는다.
/// </summary>
public static class ComposeFolder
{
    /// <summary>지문 기록 — 연출 층 폴더 안. 게임 yarn의 파일 이름 → 바이트의 SHA-256.</summary>
    public const string RecordFileName = ".composed.json";

    public const string GameLayer = "게임";
    public const string StagingLayer = "연출 층";
    public const string RetiredLayer = "보관소";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static ComposeOutcome Run(
        string stagingFolder, ComposeSettings settings, bool force = false, bool dryRun = false, StageCommandSpecTable? specs = null)
    {
        ArgumentNullException.ThrowIfNull(stagingFolder);
        ArgumentNullException.ThrowIfNull(settings);

        string dialogueFolder = settings.DialogueFolder(stagingFolder);
        string gameFolder = settings.GameFolder(stagingFolder);

        Require(stagingFolder, "연출 층 폴더");
        Require(dialogueFolder, "대사 층 폴더");
        Require(gameFolder, "게임 폴더");

        Dictionary<string, ScriptFile> dialogue = ReadLayer(dialogueFolder, retired: false);
        Dictionary<string, ScriptFile> staging = ReadLayer(stagingFolder, retired: false);
        Dictionary<string, ScriptFile> retired = ReadLayer(stagingFolder, retired: true);

        Dictionary<string, string> dialogueLines = new(StringComparer.Ordinal);

        foreach (LayerItem item in dialogue.Values.SelectMany(f => LayerScript.Parse(f.Text).Nodes).SelectMany(n => n.Items))
        {
            if (item.LineId is { } lineId)
            {
                dialogueLines.TryAdd(lineId, item.Raw);
            }
        }

        int lines = dialogueLines.Count;

        // 대사 층이 비었으면 연출 층이 통째로 보관소로 간다 — 폴더를 잘못 짚은 것으로 보고 멈춘다.
        if (dialogue.Count == 0)
        {
            return new ComposeOutcome(
                ComposeResult.Rejected([$"대사 층에 yarn이 없다 — {dialogueFolder}"]), [], 0, 0, 0, dryRun);
        }

        ComposeResult result = LayerComposer.Compose(Texts(dialogue), Texts(staging), Texts(retired), specs);

        if (!result.Ok)
        {
            return new ComposeOutcome(result, [], dialogue.Count, lines, 0, dryRun);
        }

        List<ComposeFileNote> files = [];
        int unchanged = 0;

        // BOM은 있던 대로 — 새 파일은 같은 이름의 연출 층 · 대사 층을 따른다.
        bool BomOf(string name) => staging.GetValueOrDefault(name)?.HasBom ?? dialogue.GetValueOrDefault(name)?.HasBom ?? false;

        foreach ((string name, string? text) in result.Retired)
        {
            Put(RetiredLayer, Path.Combine(stagingFolder, name), name, text, retired.GetValueOrDefault(name)?.HasBom ?? false);
        }

        foreach ((string name, string? text) in result.Staging)
        {
            Put(StagingLayer, Path.Combine(stagingFolder, name), name, text, BomOf(name));
        }

        Dictionary<string, string> record = ReadRecord(stagingFolder);
        Dictionary<string, string> newRecord = new(record, StringComparer.Ordinal);

        foreach ((string name, string? text) in result.Game)
        {
            string path = Path.Combine(gameFolder, name);
            byte[]? current = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[]? wanted = text is null ? null : Encode(text, BomOf(name));

            if (current is null && wanted is null)
            {
                newRecord.Remove(name);
                continue;
            }

            if (current is not null && wanted is not null && current.AsSpan().SequenceEqual(wanted))
            {
                newRecord[name] = Fingerprint(wanted);
                unchanged++;
                continue;
            }

            // 있는 파일을 바꾸거나 걷으려면 우리가 쓴 그대로여야 한다.
            if (current is not null && !force)
            {
                string? reason = !record.TryGetValue(name, out string? known)
                    ? "합치기가 쓴 기록이 없는 파일이다"
                    : known != Fingerprint(current) ? "마지막으로 합친 뒤에 누가 고쳤다" : null;

                if (reason is not null)
                {
                    files.Add(new ComposeFileNote(GameLayer, name, FileChange.Held, reason));
                    continue;
                }
            }

            if (wanted is null)
            {
                if (!dryRun)
                {
                    File.Delete(path);
                }

                newRecord.Remove(name);
                files.Add(new ComposeFileNote(GameLayer, name, FileChange.Removed));
                continue;
            }

            if (!dryRun)
            {
                File.WriteAllBytes(path, wanted);
            }

            newRecord[name] = Fingerprint(wanted);
            files.Add(new ComposeFileNote(GameLayer, name, FileChange.Written));
        }

        if (!dryRun && !Same(record, newRecord))
        {
            WriteRecord(stagingFolder, newRecord);
        }

        return new ComposeOutcome(result, files, dialogue.Count, lines, unchanged, dryRun) { DialogueLines = dialogueLines };

        // 층의 파일 하나 — 바이트가 다를 때만 쓰고, 글이 없으면 걷는다.
        void Put(string layer, string path, string name, string? text, bool bom)
        {
            byte[]? current = File.Exists(path) ? File.ReadAllBytes(path) : null;

            if (text is null)
            {
                if (current is null)
                {
                    return;
                }

                if (!dryRun)
                {
                    File.Delete(path);
                }

                files.Add(new ComposeFileNote(layer, name, FileChange.Removed));
                return;
            }

            byte[] wanted = Encode(text, bom);

            if (current is not null && current.AsSpan().SequenceEqual(wanted))
            {
                unchanged++;
                return;
            }

            if (!dryRun)
            {
                File.WriteAllBytes(path, wanted);
            }

            files.Add(new ComposeFileNote(layer, name, FileChange.Written));
        }
    }

    private static void Require(string folder, string what)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"{what}가 없다 — {folder}");
        }
    }

    // 폴더 바로 아래의 yarn — 보관소(.retired.yarn)와 대본을 가른다.
    private static Dictionary<string, ScriptFile> ReadLayer(string folder, bool retired)
        => Directory.GetFiles(folder, "*.yarn")
            .Where(p => LayerComposer.IsRetiredName(Path.GetFileName(p)) == retired)
            .ToDictionary(p => Path.GetFileName(p)!, ScriptFile.Load, StringComparer.Ordinal);

    private static Dictionary<string, string> Texts(Dictionary<string, ScriptFile> files)
        => files.ToDictionary(f => f.Key, f => f.Value.Text, StringComparer.Ordinal);

    private static byte[] Encode(string text, bool bom)
    {
        byte[] body = new System.Text.UTF8Encoding(false).GetBytes(text);
        return bom ? [.. Bom, .. body] : body;
    }

    private static string Fingerprint(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    private static Dictionary<string, string> ReadRecord(string stagingFolder)
    {
        string path = Path.Combine(stagingFolder, RecordFileName);

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            RecordDto? dto = JsonSerializer.Deserialize<RecordDto>(File.ReadAllText(path), Json);
            return dto?.Files is { } files
                ? new Dictionary<string, string>(files, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} — 지문 기록을 읽지 못했다: {exception.Message}");
        }
    }

    private static void WriteRecord(string stagingFolder, Dictionary<string, string> record)
    {
        RecordDto dto = new() { Files = new SortedDictionary<string, string>(record, StringComparer.Ordinal) };
        File.WriteAllText(Path.Combine(stagingFolder, RecordFileName), JsonSerializer.Serialize(dto, Json) + "\n");
    }

    private sealed class RecordDto
    {
        public SortedDictionary<string, string>? Files { get; set; }
    }
}
