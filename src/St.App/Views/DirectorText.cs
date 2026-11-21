using System.Text.RegularExpressions;

namespace St.App.Views;

/// <summary>
/// 도메인이 적는 까닭(명령줄과 같은 글)을 화면의 말로 — 연출가는 yarn의 표기를 보지 않는다(docs/PLAN.md §1.2).
/// <c>&lt;&lt;face_swap&gt;&gt;의 '길이'(duration) 칸</c> → <c>face_swap의 '길이' 칸</c>.
/// </summary>
internal static partial class DirectorText
{
    [GeneratedRegex(@"<<\s*(?:at\s+\S+\s+)?(?<name>[A-Za-z_]+)[^>]*>>")]
    private static partial Regex CommandPattern();

    [GeneratedRegex(@"(?<='[^']*')\((?<key>[a-z_]+)\)")]
    private static partial Regex KeyPattern();

    public static string Clean(string message)
    {
        string text = CommandPattern().Replace(message, m => m.Groups["name"].Value);
        text = KeyPattern().Replace(text, string.Empty);
        return text.Replace("시간 토큰", "시간").Replace("명세 표", "커맨드 표");
    }
}
