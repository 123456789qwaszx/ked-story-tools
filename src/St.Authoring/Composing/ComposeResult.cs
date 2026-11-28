namespace St.Authoring.Composing;

/// <summary>합치기가 줄 하나에 대해 알리는 것.</summary>
public enum LineNoteKind
{
    /// <summary>작가가 글이나 화자를 고쳤다 — 연출가가 다시 볼 것. 연출 층에는 옛 글이 기준으로 남는다.</summary>
    Review,

    /// <summary>새 줄 — 연출이 없다.</summary>
    NoStaging,

    /// <summary>대사 층에서 사라진 줄 — 연출을 보관소로 옮겼다.</summary>
    Retired,

    /// <summary>보관소에 있던 줄 ID가 되살아났다 — 연출을 돌려 붙였다.</summary>
    Restored,

    /// <summary>줄이 다른 노드나 장면으로 갔다 — 연출이 따라갔다.</summary>
    Moved,
}

/// <summary>줄 하나에 대한 알림.</summary>
/// <param name="LineId">줄 ID — 접두사가 든 전체(<c>line:abc</c>).</param>
/// <param name="File">그 줄이 지금 있는 파일의 이름(보관소로 간 줄은 있던 연출 층 파일).</param>
/// <param name="Node">그 줄이 지금 있는 노드(보관소로 간 줄은 있던 노드).</param>
/// <param name="OldText">연출 층의 글(기준) — 박자 표시와 태그를 뺀 화자 + 글. 다시 볼 것에만.</param>
/// <param name="NewText">대사 층의 글. 다시 볼 것에만.</param>
/// <param name="BeatsMoved">말 도중의 박자를 줄 끝으로 옮겼다 — 박자 다시 놓을 것(L5).</param>
public sealed record LineNote(
    LineNoteKind Kind, string LineId, string File, string Node, string? OldText = null, string? NewText = null, bool BeatsMoved = false);

/// <summary>
/// 합치기의 결과(docs/PLAN.md §2.5 I1). 글만 든다 — 디스크에 쓰는 일은 부르는 쪽의 것이다.
/// 세 묶음은 파일 이름 → 글이고, <b>글이 null이면 그 파일은 없어져야 한다</b>(대사 층에서 장면이 사라졌다 · 보관소가 비었다).
/// </summary>
/// <param name="Game">게임 yarn — 런타임이 읽을 완성본.</param>
/// <param name="Staging">새 연출 층 — 뼈대를 대사 층에 맞춘 것. 글이 바뀐 줄은 옛 글이 남는다.</param>
/// <param name="Retired">새 보관소.</param>
/// <param name="Notes">줄마다의 알림 — 대사 층의 차례, 그 뒤에 보관소로 간 줄.</param>
/// <param name="Errors">합치지 못한 까닭. 하나라도 있으면 세 묶음은 비어 있다 — 아무것도 쓰지 않는다.</param>
public sealed record ComposeResult(
    IReadOnlyDictionary<string, string?> Game,
    IReadOnlyDictionary<string, string?> Staging,
    IReadOnlyDictionary<string, string?> Retired,
    IReadOnlyList<LineNote> Notes,
    IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;

    internal static ComposeResult Rejected(IReadOnlyList<string> errors)
        => new(new Dictionary<string, string?>(), new Dictionary<string, string?>(), new Dictionary<string, string?>(), [], errors);
}
