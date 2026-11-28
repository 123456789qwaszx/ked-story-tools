using System.Text;
using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace St.Authoring.Composing;

/// <summary>대사 글 안의 박자 표시 하나 — 원문 그대로의 글 · 이름 · 표시를 뺀 글에서의 자리.</summary>
internal readonly record struct Beat(string Raw, string Name, int Position);

/// <summary>
/// 대사 줄 하나를 가른 것 — 들여쓰기 · 글(화자와 박자 표시가 든 원문) · 태그 꼬리 · 줄 끝 문자(docs/PLAN.md §2.5 I1).
/// 합치기는 이 조각을 그대로 옮겨 적는다 — 뜻을 풀지 않는다(이스케이프 · 서식은 디렉터가 쓴 그대로다).
/// 박자 표시를 가르는 법은 코어의 <see cref="YarnCommandTextExtractor.StripLineMarkers"/>와 같다 — 시험이 둘을 맞댄다.
/// </summary>
internal sealed partial class LineText
{
    // 줄 끝의 태그 — 공백 뒤의 #이름이 이어진 것. 역슬래시로 막은 \#은 공백 뒤가 아니라 걸리지 않는다.
    [GeneratedRegex(@"(?:[ \t]+#[^\s#]+)+[ \t]*$")]
    private static partial Regex TagTailPattern();

    // 박자 표시 [beat=이름/] — 코어의 TryReadBeatMarker와 같은 꼴(공백 허용 · 따옴표 이름).
    private static readonly Regex BeatPattern = new(
        @"\G\[\s*" + Regex.Escape(LineBeatSchedule.MarkerName) + @"\s*=\s*(?:""(?<quoted>(?:\\.|[^""])*)""|(?!"")(?<bare>[^\s/\]]*))\s*/\s*\]",
        RegexOptions.CultureInvariant);

    private readonly int _rawBodyStart;

    private LineText(
        string indent, string text, string tail, string eol, string? lineId, string plain, int bodyStart, int rawBodyStart, IReadOnlyList<Beat> beats)
    {
        Indent = indent;
        Text = text;
        Tail = tail;
        Eol = eol;
        LineId = lineId;
        Plain = plain;
        BodyStart = bodyStart;
        _rawBodyStart = rawBodyStart;
        Beats = beats;
    }

    /// <summary>줄 머리의 공백.</summary>
    public string Indent { get; }

    /// <summary>태그를 뺀 글 — 화자 · 박자 표시가 든 원문.</summary>
    public string Text { get; }

    /// <summary>글 뒤의 나머지 — 태그(앞의 공백부터)와 줄 끝 주석. 줄 끝 문자는 들지 않는다.</summary>
    public string Tail { get; }

    /// <summary>줄 끝 문자 — <c>\n</c> · <c>\r\n</c>, 파일의 마지막 줄이면 빈 글일 수 있다.</summary>
    public string Eol { get; }

    /// <summary>줄 ID — 접두사가 든 전체(<c>line:abc</c>). 태그가 없으면 null.</summary>
    public string? LineId { get; }

    /// <summary>박자 표시를 뺀 글.</summary>
    public string Plain { get; }

    /// <summary><see cref="Plain"/>에서 화자 다음 — 화자가 없으면 0.</summary>
    public int BodyStart { get; }

    public IReadOnlyList<Beat> Beats { get; }

    /// <summary>화자와 그 뒤의 쌍점 · 공백 — 없으면 빈 글.</summary>
    public string SpeakerPrefix => Plain[..BodyStart];

    /// <summary>화자의 이름 — 견줄 때 쓴다.</summary>
    public string Speaker => SpeakerPrefix.TrimEnd().TrimEnd(':').TrimEnd();

    /// <summary>화자와 박자 표시를 뺀 글.</summary>
    public string Body => Plain[BodyStart..];

    /// <summary>화자 다음의 원문 — 박자 표시가 제자리에 든 채.</summary>
    public string RawBody => Text[_rawBodyStart..];

    /// <summary>글 첫머리의 박자(대사창이 열리기 전의 앞 앵커 따위) — 글이 바뀌어도 자리가 그대로다.</summary>
    public IEnumerable<Beat> HeadBeats => Beats.Where(b => b.Position <= BodyStart);

    /// <summary>말 도중의 박자 — 글이 바뀌면 자리를 잃는다(L5).</summary>
    public IEnumerable<Beat> MidBeats => Beats.Where(b => b.Position > BodyStart && b.Position < Plain.Length);

    /// <summary>글 끝의 박자.</summary>
    public IEnumerable<Beat> EndBeats => Beats.Where(b => b.Position > BodyStart && b.Position >= Plain.Length);

    /// <summary>물리 줄 하나(줄 끝 문자까지)를 가른다.</summary>
    public static LineText Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        string eol = raw.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : raw.EndsWith('\n') ? "\n" : string.Empty;
        string content = raw[..^eol.Length];

        // 줄 읽기는 주석을 벗기고 본다 — 주석은 꼬리에 실어 그대로 옮긴다.
        int comment = content.IndexOf("//", StringComparison.Ordinal);
        string code = comment < 0 ? content : content[..comment];
        string trailing = comment < 0 ? string.Empty : content[comment..];

        string indent = code[..(code.Length - code.TrimStart().Length)];
        string rest = code[indent.Length..];

        Match tags = TagTailPattern().Match(rest);
        string text = tags.Success ? rest[..tags.Index] : rest.TrimEnd();
        string tail = rest[text.Length..] + trailing;

        string? lineId = null;

        if (tags.Success)
        {
            foreach (string tag in tags.Value.Split('#', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (tag.StartsWith("line:", StringComparison.Ordinal))
                {
                    lineId = tag;
                }
            }
        }

        (string plain, List<Beat> beats, List<int> rawOf) = Scan(text);
        int bodyStart = YarnCommandTextExtractor.CharacterPrefixLength(plain);
        int rawBodyStart = bodyStart == 0 ? 0 : rawOf[bodyStart - 1] + 1;

        return new LineText(indent, text, tail, eol, lineId, plain, bodyStart, rawBodyStart, beats);
    }

    /// <summary>
    /// 박자 하나를 글의 다른 자리로 옮긴 줄(줄 끝 문자 없이) — 글은 그대로, 표지만 움직인다(P4 · L5).
    /// <paramref name="position"/>은 화자를 뺀 글에서 "이 글자가 드러나기 직전"이고 글의 길이면 글 끝이다.
    /// 글 첫머리(0)에는 놓지 않는다 — 거기는 대사창이 열리기 전의 앞 앵커 자리다. 놓을 수 없으면 null.
    /// </summary>
    public string? WithBeatAt(int index, int position)
    {
        if (index < 0 || index >= Beats.Count || Beats[index].Position <= BodyStart || position < 1 || position > Body.Length)
        {
            return null;
        }

        int target = BodyStart + position;
        StringBuilder text = new(Text.Length);

        for (int i = 0; i <= Plain.Length; i++)
        {
            for (int b = 0; b < Beats.Count; b++)
            {
                if (b != index && Beats[b].Position == i)
                {
                    text.Append(Beats[b].Raw);
                }
            }

            if (i == target)
            {
                text.Append(Beats[index].Raw);
            }

            if (i < Plain.Length)
            {
                text.Append(Plain[i]);
            }
        }

        // 표시 곁의 공백 하나는 읽을 때 먹힌다 — 옮긴 뒤에도 글과 박자의 자리가 뜻한 대로인지 다시 읽어 본다.
        string moved = Indent + text + Tail;
        LineText reread = Parse(moved);

        return reread.Plain == Plain && reread.Beats.Count == Beats.Count && reread.Beats.Any(b => b.Raw == Beats[index].Raw && b.Position == target)
            ? moved
            : null;
    }

    // 박자 표시를 빼며 읽는다. rawOf[i]는 뺀 글의 i번째 글자가 원문의 어디였는가.
    private static (string Plain, List<Beat> Beats, List<int> RawOf) Scan(string text)
    {
        StringBuilder plain = new(text.Length);
        List<Beat> beats = [];
        List<int> rawOf = new(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            Match marker = text[i] == '[' ? BeatPattern.Match(text, i) : Match.Empty;

            if (!marker.Success)
            {
                rawOf.Add(i);
                plain.Append(text[i++]);
                continue;
            }

            string name = marker.Groups["bare"].Success
                ? marker.Groups["bare"].Value
                : Regex.Replace(marker.Groups["quoted"].Value, @"\\(.)", "$1");
            beats.Add(new Beat(marker.Value, name, plain.Length));

            // 코어와 같다 — 표시 앞이 공백(이나 글의 머리)이고 뒤도 공백이면 뒤의 공백 하나를 먹는다.
            bool afterWhitespace = plain.Length == 0 || char.IsWhiteSpace(plain[^1]);
            i = marker.Index + marker.Length;

            if (afterWhitespace && i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
        }

        return (plain.ToString(), beats, rawOf);
    }
}
