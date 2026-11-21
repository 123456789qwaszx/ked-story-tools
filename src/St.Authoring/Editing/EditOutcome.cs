using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>조작 하나의 결과. 거절이면 <see cref="Text"/>는 받은 글 그대로다 — 깨진 yarn은 내지 않는다.</summary>
public sealed class EditOutcome
{
    private EditOutcome(bool ok, string text)
    {
        Ok = ok;
        Text = text;
    }

    /// <summary>받아들였는가. 거절의 까닭은 <see cref="Error"/>.</summary>
    public bool Ok { get; }

    /// <summary>글이 바뀌었는가. 같은 값으로 고치면 받아들이되 바뀌지 않는다 — 되돌리기에 쌓지 않는다.</summary>
    public bool Changed { get; private init; }

    /// <summary>고친 뒤의 yarn 원문.</summary>
    public string Text { get; }

    public string Error { get; private init; } = string.Empty;

    /// <summary>받아들였지만 알릴 것 — 커맨드가 하나도 남지 않은 표지 같은 것.</summary>
    public IReadOnlyList<string> Warnings { get; private init; } = [];

    /// <summary>고친 뒤 다시 읽은 그 줄. 거절이면 null.</summary>
    public LineRead? Line { get; private init; }

    /// <summary>고친 커맨드의 지금 주소. 뺐거나 커맨드가 대상이 아니면(간격) null.</summary>
    public CommandAddress? Target { get; private init; }

    /// <summary>묶음에서 사라진 줄(들여쓰기 · 줄바꿈을 뗀 글). 없으면 null.</summary>
    public string? Removed { get; private init; }

    /// <summary>묶음에 새로 적힌 줄. 없으면 null.</summary>
    public string? Added { get; private init; }

    internal static EditOutcome Refused(string text, string error)
        => new(false, text) { Error = error };

    internal static EditOutcome Unchanged(string text, LineRead line, CommandAddress? target)
        => new(true, text) { Line = line, Target = target };

    internal static EditOutcome Applied(
        string text, LineRead line, CommandAddress? target, string? removed, string? added, IReadOnlyList<string> warnings)
        => new(true, text)
        {
            Changed = true,
            Line = line,
            Target = target,
            Removed = removed,
            Added = added,
            Warnings = warnings,
        };
}
