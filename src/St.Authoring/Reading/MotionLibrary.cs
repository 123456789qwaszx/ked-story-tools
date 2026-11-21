using System.Text.Json;
using Ked.Presentation.Core;
using St.Authoring.Motions;

namespace St.Authoring.Reading;

/// <summary>모션이 사는 곳.</summary>
public enum MotionSource
{
    /// <summary>모두가 쓰는 것 — 대본 곁의 <c>motions.json</c>. 프리셋이다.</summary>
    Shared,

    /// <summary>그 편의 것 — <c>&lt;대본 이름&gt;.motions.json</c>.</summary>
    Episode,
}

/// <summary>모션의 커브 하나 — 노드의 한 종류(위치 · 크기 · 회전)의 한 축.</summary>
public sealed record MotionCurve(string Node, MotionChannelKind Kind, char Axis, CurveKey[] Keys)
{
    /// <summary>화면의 이름 — "위치 Y".</summary>
    public string Label => $"{KindLabel(Kind)} {Axis}";

    /// <summary>그 시각(초)의 값.</summary>
    public float Evaluate(float seconds) => CurveFunctions.Evaluate(Keys, seconds);

    /// <summary>키들의 가장 작은 값과 가장 큰 값.</summary>
    public (float Min, float Max) Range => (Keys.Min(k => k.Value), Keys.Max(k => k.Value));

    private static string KindLabel(MotionChannelKind kind) => kind switch
    {
        MotionChannelKind.Position => "위치",
        MotionChannelKind.Scale => "크기",
        _ => "회전",
    };
}

/// <summary>읽은 모션 하나 — 클립과 그것이 온 파일.</summary>
public sealed record MotionEntry(MotionClip Clip, MotionSource Source, string Path)
{
    /// <summary>커브들 — 채널의 차례, 그 안에서 X · Y · Z.</summary>
    public IReadOnlyList<MotionCurve> Curves { get; } = Flatten(Clip);

    private static List<MotionCurve> Flatten(MotionClip clip)
    {
        List<MotionCurve> curves = [];

        foreach (MotionChannel channel in clip.Channels ?? [])
        {
            if (channel.HasX)
            {
                curves.Add(new MotionCurve(channel.Node, channel.Kind, 'X', channel.X));
            }

            if (channel.HasY)
            {
                curves.Add(new MotionCurve(channel.Node, channel.Kind, 'Y', channel.Y));
            }

            if (channel.HasZ)
            {
                curves.Add(new MotionCurve(channel.Node, channel.Kind, 'Z', channel.Z));
            }
        }

        return curves;
    }
}

/// <summary>
/// 대본이 쓰는 모션 — 대본 곁의 <c>motions.json</c>(모두가 쓰는 것)과 <c>&lt;대본 이름&gt;.motions.json</c>(그 편의 것)에서 읽는다
/// (<c>motions/2</c> — docs/PLAN.md §4.6 · §4.8). 타임라인이 그 길이를 막대로, 커브를 막대 아래에 펴 보인다.
/// 읽기만 한다. 이모지는 아직 읽지 않는다 — 점으로 보인다.
/// </summary>
public sealed class MotionLibrary
{
    /// <summary>모션을 하나도 모르는 것 — 대본 곁에 모션 파일이 없을 때.</summary>
    public static readonly MotionLibrary None = new(new Dictionary<string, MotionEntry>());

    private const string MotionCommand = "motion";
    private const string MotionCatalog = "motion";

    private readonly IReadOnlyDictionary<string, MotionEntry> _motions;

    private MotionLibrary(IReadOnlyDictionary<string, MotionEntry> motions) => _motions = motions;

    /// <summary>아는 모션의 수.</summary>
    public int Count => _motions.Count;

    /// <summary>
    /// 그 대본이 쓰는 모션을 읽는다. 이름이 겹치면 그 편의 것이 이긴다. 읽지 못한 파일 · 꼴이 어긋난 모션은 건너뛴다.
    /// </summary>
    public static MotionLibrary Load(string scriptPath)
    {
        ArgumentNullException.ThrowIfNull(scriptPath);

        Dictionary<string, MotionEntry> motions = new(StringComparer.Ordinal);

        (string Path, MotionSource Source)[] files =
        [
            (MotionFile.SharedPath(scriptPath), MotionSource.Shared),
            (MotionFile.EpisodePath(scriptPath), MotionSource.Episode),
        ];

        foreach ((string path, MotionSource source) in files)
        {

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("motions", out JsonElement list)
                    || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement element in list.EnumerateArray())
                {
                    if (ReadClip(element) is { } clip)
                    {
                        motions[clip.Name] = new MotionEntry(clip, source, path);
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // 모션 파일이 깨졌으면 그 모션을 모를 뿐이다 — 막대가 점으로 보인다.
            }
        }

        return motions.Count == 0 ? None : new MotionLibrary(motions);
    }

    /// <summary>그 이름의 모션. 모르면 null.</summary>
    public MotionEntry? Find(string name) => _motions.GetValueOrDefault(name);

    /// <summary>그 커맨드가 가리키는 모션. 모션 커맨드가 아니거나 모르는 이름이면 null.</summary>
    public MotionEntry? Find(LineReadCommand command) => NameOf(command) is { } name ? Find(name) : null;

    /// <summary>화면에 편 그 커맨드가 가리키는 모션. 모션 커맨드가 아니거나 모르는 이름이면 null.</summary>
    public MotionEntry? Find(OutlineCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Name == MotionCommand && command.Args.FirstOrDefault(a => a.Spec.Catalog == MotionCatalog && a.Value.Length > 0) is { } arg
            ? Find(arg.Value)
            : null;
    }

    /// <summary>그 대본 파일에서 그 이름의 모션을 트는 커맨드의 수 — 하나뿐이면 그 줄의 것이다.</summary>
    public static int Uses(YarnFileRead file, string name)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.Lines.SelectMany(l => l.Steps).SelectMany(s => s.Commands).Count(c => NameOf(c) == name);
    }

    /// <summary>모션 커맨드가 가리키는 이름과 그 칸. 모션 커맨드가 아니거나 이름을 적지 않았으면 null.</summary>
    public static string? NameOf(LineReadCommand command) => Named(command)?.Value;

    /// <summary>모션 커맨드에서 이름을 적는 칸.</summary>
    public static BoundArg? Named(LineReadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Name == MotionCommand)
        {
            foreach (BoundArg arg in command.Bound)
            {
                if (arg.Spec.Catalog == MotionCatalog && arg.Value is { Length: > 0 })
                {
                    return arg;
                }
            }
        }

        return null;
    }

    /// <summary>그 커맨드의 길이(초). 모르면 null — <see cref="LineTimetable.Build"/>의 길이를 묻는 함수로 넘긴다.</summary>
    public float? LengthOf(LineReadCommand command) => Find(command)?.Clip.Duration;

    // ── motions/2 읽기 ──────────────────────────────────────────────────────

    private static MotionClip? ReadClip(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || Text(element, "name") is not { Length: > 0 } name
            || Number(element, "duration") is not > 0f)
        {
            return null;
        }

        List<MotionChannel> channels = [];

        if (element.TryGetProperty("channels", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement channel in list.EnumerateArray())
            {
                if (channel.ValueKind == JsonValueKind.Object && Text(channel, "node") is { Length: > 0 } node
                    && Enum.TryParse(Text(channel, "kind"), ignoreCase: true, out MotionChannelKind kind))
                {
                    channels.Add(new MotionChannel { Node = node, Kind = kind, X = Keys(channel, "x"), Y = Keys(channel, "y"), Z = Keys(channel, "z") });
                }
            }
        }

        return new MotionClip
        {
            Name = name,
            Rig = Text(element, "rig") ?? string.Empty,
            Duration = Number(element, "duration") ?? 0f,
            End = Enum.TryParse(Text(element, "end"), ignoreCase: true, out MotionEnd end) ? end : MotionEnd.Restore,
            Channels = [.. channels],
        };
    }

    private static CurveKey[] Keys(JsonElement channel, string axis)
    {
        if (!channel.TryGetProperty(axis, out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<CurveKey> keys = [];

        foreach (JsonElement key in list.EnumerateArray())
        {
            if (key.ValueKind == JsonValueKind.Object && Number(key, "t") is { } time && Number(key, "v") is { } value)
            {
                keys.Add(new CurveKey(time, value, Number(key, "inTangent") ?? 0f, Number(key, "outTangent") ?? 0f));
            }
        }

        return [.. keys];
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static float? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : null;
}
