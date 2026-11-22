using Ked.Presentation.Core;

namespace St.Authoring.Tests;

/// <summary>
/// 시험의 대본 — 지어낸 글뿐이다(대사 원문이 든 대본은 이 저장소에 넣지 않는다).
/// 런타임의 데모 대본은 <c>Fixtures/</c>에 그대로 있다.
/// </summary>
internal static class Scripts
{
    /// <summary>줄 넷 — t1 새 표기(앞 앵커 in · 글 표지 a · 커맨드 없는 표지 b), t2 at 없는 명령이 든 줄(<c>&lt;&lt;at start&gt;&gt;</c> 하나와
    /// 실행되지 않는 at 없는 명령 하나 — 읽기만), t3 들여쓴 줄, t4 묶음이 없는 줄.</summary>
    public const string Sample =
        "title: T\n" +
        "---\n" +
        "<<at in fade_out s1 15fr>>\n" +
        "<<at in pause 15fr>>\n" +
        "<<at in face s2 05>>\n" +
        "<<gap in 1s>>\n" +
        "// 주석은 그대로다\n" +
        "<<at start face s2 05>>\n" +
        "<<at a face_swap s2 03 13fr>> // 줄 끝 주석\n" +
        "<<at end emoji s2 star>>\n" +
        "가: [beat=in/]하나 [beat=a/]둘 [beat=b/]셋 #line:t1\n" +
        "\n" +
        "<<at start fade_in s1 10fr>>\n" +
        "<<fade_out s2 10fr>>\n" +
        "나: 실행되지 않는 명령 #line:t2\n" +
        "\n" +
        "    <<at start face s1 3>>\n" +
        "    나: 들여쓴 줄 #line:t3\n" +
        "다: 묶음이 없는 줄 #line:t4\n" +
        "라: LineId가 없는 줄\n" +
        "===\n";

    public static string Demo => File.ReadAllText(DemoPath);

    public static string DemoPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "presentation_demo_Demo_Presentation.yarn");

    /// <summary>데모 대본에서 새 표기로 쓴 줄 — 글 표지 a · b에 face_swap이 하나씩 있다.</summary>
    public const string DemoLine = "ln_k7inl2cm";

    public static LineRead Read(string text, string lineId)
        => YarnLineReader.Read(text, lineId) ?? throw new InvalidOperationException($"{lineId}가 없다");

    /// <summary>그 줄의 커맨드를 읽히는 차례로 — "앵커 이름 인자… +지연".</summary>
    public static string[] Commands(LineRead line)
        => line.Steps
            .SelectMany(s => s.Commands.Select(c => FormattableString.Invariant($"{s.Name} {c.Name} {string.Join(' ', c.Args)} +{c.Delay:0.###}")))
            .ToArray();

    /// <summary>줄바꿈을 CRLF로 바꾼 것 — Windows에서 체크아웃한 대본.</summary>
    public static string Crlf(string text) => text.Replace("\n", "\r\n");
}
