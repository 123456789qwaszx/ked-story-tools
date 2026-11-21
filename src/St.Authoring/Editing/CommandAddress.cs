using System.Globalization;
using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>
/// 커맨드의 주소 — <c>LineId / 앵커 / 종류 / 순번</c>(docs/PLAN.md §3).
/// 파일의 줄 번호는 주소가 아니다 — 고치면 밀린다. 순번은 그 앵커의 같은 종류 안에서 몇 번째인가(0부터)이고,
/// 줄 읽기(<see cref="LineReadCommand.KindIndex"/>)가 센다.
/// </summary>
public readonly record struct CommandAddress(string LineId, string Anchor, CommandKind Kind, int Index)
{
    /// <summary>글로 쓴 주소 — <c>ln_abc/a/acting/0</c>. LineId의 <c>line:</c> 접두사는 떼고 적는다.</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{ShortLineId(LineId)}/{Anchor}/{CommandKinds.Key(Kind)}/{Index}");

    /// <summary>줄 읽기가 낸 커맨드의 주소. 명세 표에 없는 커맨드는 종류가 없어 주소도 없다.</summary>
    public static CommandAddress? Of(LineRead line, LineReadCommand command)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(command);

        if (line.LineId is null || command.Spec is null || command.Anchor is null || command.KindIndex < 0)
        {
            return null;
        }

        return new CommandAddress(line.LineId, command.Anchor, command.Spec.Kind, command.KindIndex);
    }

    /// <summary><c>LineId/앵커/종류/순번</c>을 읽는다. 종류는 명세 표의 키(camera · placement · acting · screen · setup · sound · time).</summary>
    public static bool TryParse(string? text, out CommandAddress address, out string error)
    {
        address = default;
        error = string.Empty;

        string[] parts = (text ?? string.Empty).Trim().Split('/');

        if (parts.Length != 4 || parts.Any(string.IsNullOrWhiteSpace))
        {
            error = $"주소 '{text}'가 LineId/앵커/종류/순번의 꼴이 아니다 (예: ln_abc/start/acting/0)";
            return false;
        }

        if (!CommandKinds.TryParse(parts[2], out CommandKind kind))
        {
            error = $"종류 '{parts[2]}'를 모른다 — {string.Join(" · ", CommandKinds.All.Select(CommandKinds.Key))}";
            return false;
        }

        if (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            error = $"순번 '{parts[3]}'이 0 이상의 수가 아니다";
            return false;
        }

        address = new CommandAddress(YarnLineReader.NormalizeLineId(parts[0]), parts[1], kind, index);
        return true;
    }

    /// <summary><c>line:abc</c> → <c>abc</c>. 사람과 AI가 쓰고 읽는 꼴이다 — 대본의 <c>#line:abc</c>에서 <c>#line:</c>을 뗀 것.</summary>
    public static string ShortLineId(string? lineId)
    {
        const string prefix = "line:";
        string id = lineId ?? string.Empty;
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : id;
    }
}
