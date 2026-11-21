using Ked.Presentation.Core;

namespace St.Authoring.Editing;

/// <summary>
/// 줄 고치기의 조작 하나(docs/PLAN.md §4.3 · P4) — 다섯 가지뿐이다.
/// 앵커를 새로 만들거나 옮기는 일은 없다: 표지는 대사 글 안에 있고, 이 툴은 글을 건드리지 않는다.
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
/// </summary>
public sealed record AddCommand(string LineId, string Anchor, string Name, IReadOnlyList<string> Args, int? KindIndex = null)
    : LineEdit(LineId);

/// <summary>커맨드 빼기 — 그 <c>&lt;&lt;at&gt;&gt;</c> 한 줄.</summary>
public sealed record RemoveCommand(CommandAddress Target) : LineEdit(Target.LineId);

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
