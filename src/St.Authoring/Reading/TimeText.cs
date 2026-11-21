using System.Globalization;
using Ked.Presentation.Core;

namespace St.Authoring.Reading;

/// <summary>초를 화면의 글로 — 프레임(30fps)에 맞아떨어지면 프레임을 함께 적는다. 타임라인의 눈금이 프레임이다.</summary>
public static class TimeText
{
    /// <summary><c>0.5s · 15fr</c>, 프레임에 맞지 않으면 <c>0.37s</c>.</summary>
    public static string Format(float seconds)
    {
        string text = seconds.ToString("0.###", CultureInfo.InvariantCulture) + "s";
        float frames = seconds * DurationToken.FramesPerSecond;
        float rounded = MathF.Round(frames);

        return rounded >= 1f && MathF.Abs(frames - rounded) < 0.001f
            ? $"{text} · {rounded.ToString("0", CultureInfo.InvariantCulture)}fr"
            : text;
    }
}
