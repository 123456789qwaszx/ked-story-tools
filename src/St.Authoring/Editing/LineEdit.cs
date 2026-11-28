using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>
/// 줄 고치기의 조작 하나(docs/PLAN.md §4.3 · P4) — 다섯에 바꾸기(K7 · D15)를 더해 여섯, 연출 층의 확인(K8 · C7) · 박자 놓기(K8 · L5) · 보관소에서 옮기기(K8 · C3)까지 아홉이다.
/// 앵커를 새로 만들거나 옮기는 일은 없다: 표지는 대사 글 안에 있고, 이 툴은 글을 짓지 않는다 — 확인은 디렉터의 글을 받아 적을 뿐이다.
/// </summary>
public abstract record LineEdit(string LineId);

/// <summary>값 바꾸기 — 그 커맨드의 인자 칸 하나. <paramref name="Value"/>가 null이면 그 칸을 비운다(생략값으로 간다).</summary>
/// <param name="Target">고칠 커맨드.</param>
/// <param name="Arg">명세 표의 칸 키(<see cref="CommandArgSpec.Name"/>) — slot · duration · face ….</param>
/// <param name="Value">대본에 적을 글.</param>
public sealed record SetValue(CommandAddress Target, string Arg, string? Value) : LineEdit(Target.LineId);

/// <summary>
/// 커맨드 더하기 — 그 앵커에 <c>&lt;&lt;at 앵커 이름 인자…&gt;&gt;</c> 한 줄.
/// <paramref name="KindIndex"/>를 주면 그 종류의 그 순번이 되게 끼우고(뒤의 것이 밀린다), 없으면 앵커의 맨 뒤에 붙인다.
/// <paramref name="Card"/>를 주면 줄 끝에 카드의 출처를 남긴다(<c>// card:E04</c> — docs/PLAN.md P5).
/// </summary>
public sealed record AddCommand(
    string LineId, string Anchor, string Name, IReadOnlyList<string> Args, int? KindIndex = null, string? Card = null)
    : LineEdit(LineId);

/// <summary>커맨드 빼기 — 그 <c>&lt;&lt;at&gt;&gt;</c> 한 줄.</summary>
public sealed record RemoveCommand(CommandAddress Target) : LineEdit(Target.LineId);

/// <summary>
/// 커맨드 바꾸기(K7-3 · D15) — 그 <c>&lt;&lt;at&gt;&gt;</c> 한 줄을 제자리에서 다른 커맨드로 다시 쓴다. 빼고 그 자리에 더한 것과 같고 되돌리기 한 칸이다.
/// 앵커와 줄의 차례가 그대로라 앞의 <c>pause</c>(지연)가 지켜진다. 카드의 출처(<c>// card:E04</c>)는 걷는다 — 더는 그 카드의 커맨드가 아니다(U6).
/// 다른 줄 끝 주석은 그대로 둔다.
/// </summary>
public sealed record ReplaceCommand(CommandAddress Target, string Name, IReadOnlyList<string> Args) : LineEdit(Target.LineId);

/// <summary>
/// 지연 바꾸기 — 앵커가 열린 뒤 그 커맨드가 시작하기까지의 시간을 <paramref name="Delay"/>(시간 토큰 — 15fr · 0.5s)로 맞춘다.
/// 그 커맨드 바로 앞의 <c>pause</c>를 넣거나 고치거나 뺀다. <c>pause</c>는 같은 앵커의 뒤 커맨드를 모두 붙잡으므로
/// 그 뒤의 커맨드도 같은 만큼 밀리거나 당겨진다.
/// </summary>
public sealed record SetDelay(CommandAddress Target, string Delay) : LineEdit(Target.LineId);

/// <summary>
/// 간격 바꾸기 — 그 앵커가 열린 뒤 다음 앵커가 열리기까지의 최소 시간(<c>&lt;&lt;gap 앵커 길이&gt;&gt;</c>).
/// <paramref name="Gap"/>이 null이거나 0이면 간격을 걷는다.
/// </summary>
public sealed record SetGap(string LineId, string Anchor, string? Gap) : LineEdit(LineId);

/// <summary>
/// 확인(K8 · C7) — 연출 층의 대사 줄을 대사 층의 새 글로 갈아 적는다. 작가가 고친 글을 연출가가 보았다는 뜻이다 — 둘이 같아져
/// "다시 볼 것"이 사라진다. <paramref name="Line"/>은 합치기가 지은 그 줄이다(<c>LayerComposer.CompareLine</c> — 새 글에 연출 층의 박자와
/// 태그를 얹은 것, 줄 끝 문자 없이). 이 툴이 글을 짓지 않는다 — 줄 ID가 다르거나 커맨드의 읽기가 달라지면 거절한다.
/// </summary>
public sealed record AcceptText(string LineId, string Line) : LineEdit(LineId);

/// <summary>
/// 박자 놓기(K8 · L5 — P4를 고쳤다) — 대사 줄의 박자 표시를 글의 다른 자리로 옮긴다. 글은 그대로, 표지만 움직인다.
/// <paramref name="Line"/>은 표지를 옮긴 그 줄이다(<c>BeatPlacement.Move</c> — 줄 끝 문자 없이). 다시 볼 줄에서 놓으면 새 글 위에 놓는다 —
/// 그 줄은 확인된 것이다(C7). 줄 ID가 다르거나 커맨드의 읽기가 달라지면 거절한다.
/// </summary>
public sealed record PlaceBeat(string LineId, string Line) : LineEdit(LineId);

/// <summary>
/// 보관소에서 옮기기(K8 · C3) — 보관한 묶음을 이 줄의 묶음 뒤에 덧붙이고, 묶음이 가리키는 박자의 표지를 이 줄의 글에 더한다.
/// <paramref name="Lead"/>와 <paramref name="Line"/>은 <c>Shelf.Onto</c>가 지은 것이다(묶음의 원문 · 표지를 더한 대사 줄, 줄 끝 문자 없이).
/// 이 줄의 글과 지금의 묶음은 그대로다. 깨진 yarn이 되면(같은 앵커의 간격이 둘 …) 거절한다.
/// </summary>
public sealed record AdoptStaging(string LineId, string Lead, string Line) : LineEdit(LineId);
