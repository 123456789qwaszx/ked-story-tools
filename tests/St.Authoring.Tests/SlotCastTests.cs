using Ked.Presentation.Core;
using St.Authoring.Reading;

namespace St.Authoring.Tests;

/// <summary>슬롯에 누가 서 있는가 — 앞 커맨드를 원문의 차례로 셈한다.</summary>
public class SlotCastTests
{
    // 지어낸 대본. 1행 title · 2행 --- 다음부터.
    private const string Script =
        "title: A\n---\n" +
        "<<at in slot s1>>\n" +                        // 3
        "<<at in cast s1 nazuna a 06>>\n" +            // 4
        "<<at in slot s2>>\n" +                        // 5
        "<<at in cast s2 kaede>>\n" +                  // 6
        "<<at in actor hero s2>>\n" +                  // 7
        "가: [beat=in/]하나 #line:t1\n\n" +            // 8
        "<<at start face_swap s1 09>>\n" +             // 10
        "<<at start pose hero b>>\n" +                 // 11
        "나: 둘 #line:t2\n\n" +                         // 12
        "<<at start cast s1 iris>>\n" +                // 14
        "다: 셋 #line:t3\n===\n";                       // 15

    private static YarnFileRead Read() => YarnLineReader.ReadFile(Script, "a.yarn", StageCommandSpecTable.Default);

    [Fact]
    public void 앞에서_세운_캐릭터와_바뀐_포즈_표정을_안다()
    {
        IReadOnlyDictionary<string, CastInfo> cast = SlotCast.Before(Read(), 12);

        Assert.Equal(new CastInfo("s1", "nazuna", "a", "09", 4), cast["s1"]);

        // 적지 않은 포즈 · 표정은 생략값(a · 1). 별칭으로 바꾼 포즈도 그 슬롯의 것이다.
        Assert.Equal(new CastInfo("s2", "kaede", "b", "1", 6), cast["s2"]);
        Assert.Same(cast["s2"], cast["hero"]);
    }

    [Fact]
    public void 그_줄_앞까지만_센다()
    {
        // 4행의 cast 앞 — 아직 아무도 없다.
        Assert.Empty(SlotCast.Before(Read(), 4));

        // 14행에서 s1이 바뀐다 — 그 앞과 뒤.
        Assert.Equal("nazuna", SlotCast.Before(Read(), 14)["s1"].Character);
        Assert.Equal("iris", SlotCast.Before(Read(), 15)["s1"].Character);
    }

    [Fact]
    public void 화면의_글()
    {
        IReadOnlyDictionary<string, CastInfo> cast = SlotCast.Before(Read(), 12);

        Assert.Equal("s1 — nazuna · 포즈 a · 표정 09 (4행에서 세웠다)", SlotCast.Describe(cast, "s1"));
        Assert.Equal("hero(= s2) — kaede · 포즈 b · 표정 1 (6행에서 세웠다)", SlotCast.Describe(cast, "hero"));
        Assert.StartsWith("s9 — 이 앞에서 세운 캐릭터가 없다", SlotCast.Describe(cast, "s9"));
        Assert.Null(SlotCast.Describe(cast, " "));

        // 배경 슬롯은 배경의 이름을 말한다.
        YarnFileRead background = YarnLineReader.ReadFile(
            "title: B\n---\n<<at in bg_spawn bg_main room_night>>\n가: [beat=in/]하나 #line:b1\n===\n", "b.yarn", StageCommandSpecTable.Default);
        Assert.Equal("bg_main — 배경 room_night (3행에서 세웠다)", SlotCast.Describe(SlotCast.Before(background, 4), "bg_main"));
    }
}
