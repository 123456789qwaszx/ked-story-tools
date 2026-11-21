using System.Text.Json;
using Ked.Presentation.Core;

namespace St.Authoring.Reading;

/// <summary>
/// 대본에 길이가 없는 커맨드의 길이 — 모션 클립의 길이를 대본 곁의 <c>motions.json</c>에서 읽는다(docs/PLAN.md §4.6).
/// 타임라인이 그 막대의 길이로 쓴다. 이모지의 길이는 아직 읽지 않는다 — 점으로 보인다.
/// </summary>
public sealed class ClipLengths
{
    /// <summary>길이를 하나도 모르는 것 — 대본 곁에 모션 파일이 없을 때.</summary>
    public static readonly ClipLengths None = new(new Dictionary<string, float>());

    private const string MotionCommand = "motion";
    private const string MotionCatalog = "motion";
    private const string SharedFile = "motions.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, float> _motions;

    private ClipLengths(IReadOnlyDictionary<string, float> motions) => _motions = motions;

    /// <summary>아는 모션의 수.</summary>
    public int Count => _motions.Count;

    /// <summary>
    /// 그 대본이 쓰는 모션의 길이를 읽는다 — 같은 폴더의 <c>motions.json</c>(모두가 쓰는 것)과
    /// <c>&lt;대본 이름&gt;.motions.json</c>(그 편의 것). 이름이 겹치면 그 편의 것이 이긴다. 읽지 못한 파일은 건너뛴다.
    /// </summary>
    public static ClipLengths Load(string scriptPath)
    {
        ArgumentNullException.ThrowIfNull(scriptPath);

        string folder = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? string.Empty;
        Dictionary<string, float> motions = new(StringComparer.Ordinal);

        foreach (string file in new[] { SharedFile, Path.GetFileNameWithoutExtension(scriptPath) + ".motions.json" })
        {
            string path = Path.Combine(folder, file);

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                foreach (MotionDto motion in JsonSerializer.Deserialize<MotionsDto>(File.ReadAllText(path), Json)?.Motions ?? [])
                {
                    if (!string.IsNullOrEmpty(motion.Name) && motion.Duration > 0f)
                    {
                        motions[motion.Name] = motion.Duration;
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // 모션 파일이 깨졌으면 그 길이를 모를 뿐이다 — 막대가 점으로 보인다.
            }
        }

        return motions.Count == 0 ? None : new ClipLengths(motions);
    }

    /// <summary>그 커맨드의 길이(초). 모르면 null — <see cref="LineTimetable.Build"/>의 길이를 묻는 함수로 넘긴다.</summary>
    public float? LengthOf(LineReadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Name != MotionCommand)
        {
            return null;
        }

        foreach (BoundArg arg in command.Bound)
        {
            if (arg.Spec.Catalog == MotionCatalog && arg.Value is { } name && _motions.TryGetValue(name, out float length))
            {
                return length;
            }
        }

        return null;
    }

    private sealed record MotionsDto(List<MotionDto>? Motions);

    private sealed record MotionDto(string? Name, float Duration);
}
