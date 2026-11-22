using Ked.Presentation.Core;

namespace St.Authoring.Reading;

/// <summary>슬롯에 선 캐릭터 — 그 자리에서 마지막으로 본 것.</summary>
/// <param name="Slot">슬롯 키(별칭이면 가리키는 슬롯).</param>
/// <param name="Line">세운 <c>cast</c>(또는 <c>portrait</c> · <c>bg_spawn</c>)의 원문 줄 번호.</param>
/// <param name="Background">배경 슬롯인가(<c>bg_spawn</c>) — 그러면 Character가 배경의 이름이다.</param>
public sealed record CastInfo(string Slot, string Character, string Pose, string Face, int Line, bool Background = false)
{
    /// <summary>화면의 한 줄 — "s1 — nazuna · 포즈 a · 표정 06 (12행에서 세웠다)".</summary>
    public string Describe(string written)
    {
        string head = written == Slot ? Slot : $"{written}(= {Slot})";
        return Background
            ? $"{head} — 배경 {Character} ({Line}행에서 세웠다)"
            : $"{head} — {Character} · 포즈 {Pose} · 표정 {Face} ({Line}행에서 세웠다)";
    }
}

/// <summary>
/// 그 자리에서 슬롯마다 누가 서 있는가 — 같은 대본 파일의 앞 커맨드를 차례로 셈한다(docs/PLAN.md §4.5).
/// <c>cast</c>가 세우고(배경은 <c>bg_spawn</c>), <c>pose</c> · <c>face</c> · <c>face_swap</c>이 포즈 · 표정을 바꾸고, <c>actor</c>가 별칭을 붙인다.
/// 대사창의 초상은 <c>portrait</c>가 슬롯 <c>portrait</c>에 세운다. 원문의 차례로만 셈한다 — 진행이 다른 노드에서 건너오는 것은 모른다.
/// 장면마다 대본 파일이 하나이고 장면의 첫 줄이 무대를 세우므로 파일 안에서 셈하면 된다.
/// </summary>
public static class SlotCast
{
    private const string PortraitSlot = "portrait";

    /// <summary>원문의 그 줄(1부터) 앞까지 본 슬롯마다의 캐릭터. 별칭도 키로 든다.</summary>
    public static IReadOnlyDictionary<string, CastInfo> Before(YarnFileRead file, int beforeLine)
    {
        ArgumentNullException.ThrowIfNull(file);

        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        Dictionary<string, CastInfo> cast = new(StringComparer.Ordinal);

        foreach (LineReadCommand command in file.Lines
                     .SelectMany(l => l.Steps.SelectMany(s => s.Commands).Concat(l.Immediate))
                     .Where(c => c.Line > 0 && c.Line < beforeLine)
                     .OrderBy(c => c.Line))
        {
            string? Arg(string name) => command.Bound.FirstOrDefault(b => b.Spec.Name == name) is { Spec: not null } b ? b.Value : null;
            string Resolve(string slot) => aliases.TryGetValue(slot, out string? real) ? real : slot;

            switch (command.Name)
            {
                case "actor" when Arg("alias") is { Length: > 0 } alias && Arg("slot") is { Length: > 0 } target:
                    aliases[alias] = Resolve(target);
                    break;

                case "cast" when Arg("slot") is { Length: > 0 } slot && Arg("character") is { Length: > 0 } character:
                    cast[Resolve(slot)] = new CastInfo(Resolve(slot), character, Arg("pose") ?? "a", Arg("face") ?? "1", command.Line);
                    break;

                case "portrait" when Arg("character") is { Length: > 0 } character:
                    cast[PortraitSlot] = new CastInfo(PortraitSlot, character, Arg("pose") ?? "a", Arg("face") ?? "1", command.Line);
                    break;

                case "bg_spawn" when Arg("slot") is { Length: > 0 } slot && Arg("background") is { Length: > 0 } background:
                    cast[Resolve(slot)] = new CastInfo(Resolve(slot), background, string.Empty, string.Empty, command.Line, Background: true);
                    break;

                case "portrait_close":
                    cast.Remove(PortraitSlot);
                    break;

                case "pose" when Arg("slot") is { Length: > 0 } slot && cast.TryGetValue(Resolve(slot), out CastInfo? posed) && Arg("pose") is { Length: > 0 } pose:
                    cast[posed.Slot] = posed with { Pose = pose };
                    break;

                case "face" or "face_swap" when Arg("slot") is { Length: > 0 } slot && cast.TryGetValue(Resolve(slot), out CastInfo? faced) && Arg("face") is { Length: > 0 } face:
                    cast[faced.Slot] = faced with { Face = face };
                    break;
            }
        }

        // 별칭으로도 찾게 — 별칭이 가리키는 슬롯에 선 사람.
        foreach ((string alias, string slot) in aliases)
        {
            if (cast.TryGetValue(slot, out CastInfo? info))
            {
                cast.TryAdd(alias, info);
            }
        }

        return cast;
    }

    /// <summary>
    /// 슬롯 칸에 띄울 글 — 누가 서 있는가. 그 슬롯에 아직 아무도 세우지 않았으면 그렇다고 말한다.
    /// 슬롯 칸이 아니거나 비었으면 null.
    /// </summary>
    public static string? Describe(IReadOnlyDictionary<string, CastInfo> cast, string slot)
    {
        ArgumentNullException.ThrowIfNull(cast);

        if (string.IsNullOrWhiteSpace(slot))
        {
            return null;
        }

        string key = slot.Trim();
        return cast.TryGetValue(key, out CastInfo? info)
            ? info.Describe(key)
            : $"{key} — 이 앞에서 세운 캐릭터가 없다(cast · bg_spawn이 이 대본 파일에 없거나 뒤에 있다)";
    }
}
