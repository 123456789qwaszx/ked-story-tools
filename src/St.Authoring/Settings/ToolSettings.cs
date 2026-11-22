using System.Text.Json;

namespace St.Authoring.Settings;

/// <summary>
/// 툴 설정 — 대본이 아니라 이 컴퓨터의 것. JSON 한 파일, 없으면 기본값.
/// 읽기 실패는 기본값으로 떨어지되 <see cref="LoadProblem"/>에 남긴다.
/// </summary>
public sealed class ToolSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>마지막으로 연 대본의 경로(파일이나 폴더) — 다음에 켜면 다시 연다.</summary>
    public string? LastPath { get; set; }

    /// <summary>
    /// 대본마다 쓰는 카드 카탈로그의 폴더(작업공간 <c>catalog/</c> — cards.json · kinds.json이 든 곳).
    /// 열쇠는 연 대본의 경로다(docs/PLAN.md U3).
    /// </summary>
    public Dictionary<string, string> Catalogs { get; set; } = [];

    /// <summary>타자기의 기준 속도(초당 글자 수) — 타임라인이 글이 쳐지는 시간을 어림할 때 쓴다. 런타임 예제 씬의 값이 20이다.</summary>
    public const float DefaultTypewriterSpeed = 20f;

    /// <summary>대본마다의 타자기 속도. 열쇠는 연 대본의 경로다(U3). 적지 않은 대본은 <see cref="DefaultTypewriterSpeed"/>.</summary>
    public Dictionary<string, float> TypewriterSpeeds { get; set; } = [];

    /// <summary>그 대본의 타자기 속도.</summary>
    public float TypewriterSpeedFor(string root)
        => TypewriterSpeeds.TryGetValue(root, out float speed) && speed > 0f ? speed : DefaultTypewriterSpeed;

    /// <summary>줄 목록(왼쪽)에 화자의 이름을 보인다 — 설정 창의 "기타". 처음에는 보인다.</summary>
    public bool ShowSpeaker { get; set; } = true;

    /// <summary>창의 꼴 — 크기와 양쪽 칸의 폭. 한 번도 닫지 않았으면 null.</summary>
    public WindowLayout? Window { get; set; }

    /// <summary>설정 파일을 읽지 못한 까닭. 읽었거나 파일이 없으면 null.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LoadProblem { get; private set; }

    /// <summary>이 컴퓨터의 설정 파일 자리.</summary>
    public static string DefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ked-story-tools", "settings.json");

    public static ToolSettings Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return new ToolSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path), Options) ?? new ToolSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ToolSettings { LoadProblem = $"설정을 읽지 못해 기본값으로 연다 — {exception.Message}" };
        }
    }

    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}

/// <summary>창의 꼴 — 화면의 것이지만 설정 파일에 함께 남는다.</summary>
public sealed class WindowLayout
{
    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>왼쪽(줄 목록)의 폭.</summary>
    public double Left { get; set; }

    /// <summary>오른쪽(칸)의 폭.</summary>
    public double Right { get; set; }
}
