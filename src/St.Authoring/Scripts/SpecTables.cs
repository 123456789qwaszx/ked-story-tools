using System.Text.Json;
using Ked.Presentation.Core;

namespace St.Authoring.Scripts;

/// <summary>
/// 명세 표를 고른다 — 코어 사본의 표(<see cref="StageCommandSpecTable.Default"/>)이거나,
/// 런타임이 낸 <c>command-specs.json</c>이다. 포크에만 있는 어휘는 포크가 낸 파일로 읽는다(docs/PLAN.md §4.1).
/// </summary>
public static class SpecTables
{
    // 필드 이름이 JSON 키와 1:1인 전송 타입이다(유니티의 JsonUtility와 같은 모양).
    private static readonly JsonSerializerOptions Dto = new() { IncludeFields = true };

    /// <summary><c>command-specs.json</c>을 명세 표로 읽는다. 읽다 건너뛴 것은 <paramref name="problems"/>에 적힌다.</summary>
    public static StageCommandSpecTable Load(string path, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(problems);
        return Parse(File.ReadAllText(path), problems);
    }

    internal static StageCommandSpecTable Parse(string json, List<string> problems)
    {
        CommandSpecsFileDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<CommandSpecsFileDto>(json, Dto);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"command-specs.json을 읽지 못했다 — {exception.Message}");
        }

        if (dto?.schema != CommandSpecsFile.Schema)
        {
            throw new InvalidDataException($"command-specs.json의 판이 '{dto?.schema}'다 — 이 툴은 '{CommandSpecsFile.Schema}'를 읽는다");
        }

        return CommandSpecsFile.FromDto(dto, problems);
    }

    internal static string ToJson(StageCommandSpecTable table)
        => JsonSerializer.Serialize(CommandSpecsFile.ToDto(table), Dto);
}
