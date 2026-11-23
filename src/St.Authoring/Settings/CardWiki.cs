namespace St.Authoring.Settings;

/// <summary>
/// 카드의 위키 주소(K7-5 · D17) — 카드의 글(의도 · 해석 · 쓰인 자리)은 팀의 위키에 있고, 툴은 거기로 가는 링크만 든다.
/// 주소 틀은 설정에 하나(<see cref="ToolSettings.CardWiki"/>) — <c>{id}</c> 자리에 카드 ID가 들어간다
/// (<c>https://wiki.example/cards#{id}</c>). <c>{id}</c>가 없으면 끝에 <c>#ID</c>를 붙인다.
/// </summary>
public static class CardWiki
{
    public const string IdPlaceholder = "{id}";

    /// <summary>그 카드의 위키 주소. 틀이 비었거나 http · https의 온전한 주소가 되지 않으면 null.</summary>
    public static Uri? Link(string? template, string cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);

        if (string.IsNullOrWhiteSpace(template) || cardId.Length == 0)
        {
            return null;
        }

        string id = Uri.EscapeDataString(cardId);
        string text = template.Contains(IdPlaceholder, StringComparison.Ordinal)
            ? template.Trim().Replace(IdPlaceholder, id, StringComparison.Ordinal)
            : $"{template.Trim()}#{id}";

        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }
}
