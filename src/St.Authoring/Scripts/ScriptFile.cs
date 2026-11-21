using System.Text;

namespace St.Authoring.Scripts;

/// <summary>
/// 디스크의 yarn 파일 하나 — 글자로 읽고, 받은 바이트 그대로 되쓴다.
/// UTF-8만 받는다(BOM은 있던 대로 지킨다). UTF-8이 아닌 파일은 읽지 않는다 — 고치지 않은 곳의 바이트가 달라질 수 있어서다.
/// 줄 끝 문자는 글에 든 그대로다 — 바꾸지 않는다.
/// </summary>
public sealed class ScriptFile
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private ScriptFile(string path, string text, bool hasBom)
    {
        Path = path;
        Text = text;
        HasBom = hasBom;
    }

    public string Path { get; }

    /// <summary>파일의 글 — BOM은 떼어 냈다.</summary>
    public string Text { get; }

    public bool HasBom { get; }

    /// <summary>파일을 읽는다. UTF-8이 아니면 <see cref="InvalidDataException"/>.</summary>
    public static ScriptFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Decode(path, File.ReadAllBytes(path));
    }

    internal static ScriptFile Decode(string path, byte[] bytes)
    {
        bool hasBom = bytes.AsSpan().StartsWith(Bom);

        try
        {
            string text = Strict.GetString(bytes, hasBom ? Bom.Length : 0, bytes.Length - (hasBom ? Bom.Length : 0));
            return new ScriptFile(path, text, hasBom);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException($"{path} — UTF-8이 아니다. 이 툴은 UTF-8 대본만 읽고 쓴다");
        }
    }

    /// <summary>고친 글을 이 파일의 바이트로 — BOM은 읽을 때 있던 대로.</summary>
    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] body = Strict.GetBytes(text);
        return HasBom ? [.. Bom, .. body] : body;
    }

    /// <summary>고친 글을 이 파일에 쓴다.</summary>
    public void Save(string text) => File.WriteAllBytes(Path, Encode(text));
}
