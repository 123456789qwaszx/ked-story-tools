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
