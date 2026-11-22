using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Authoring.Tests;

public class CommandAddressTests
{
    [Fact]
    public void 글로_쓴_주소를_읽고_다시_쓴다()
    {
        Assert.True(CommandAddress.TryParse("ln_abc/start/acting/2", out CommandAddress address, out _));

        Assert.Equal(new CommandAddress("line:ln_abc", "start", CommandKind.Acting, 2), address);
        Assert.Equal("ln_abc/start/acting/2", address.ToString());

        Assert.True(CommandAddress.TryParse("line:ln_abc/a/time/0", out CommandAddress prefixed, out _));
        Assert.Equal("line:ln_abc", prefixed.LineId);
    }

    [Theory]
    [InlineData("ln_abc/start/acting")]
    [InlineData("ln_abc/start/연기/0")]
    [InlineData("ln_abc/start/acting/-1")]
    [InlineData("ln_abc//acting/0")]
    [InlineData("")]
    public void 꼴이_아닌_주소는_까닭과_함께_거절한다(string text)
    {
        Assert.False(CommandAddress.TryParse(text, out _, out string error));
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void 줄_읽기의_커맨드에서_주소를_낸다()
    {
        LineRead line = Scripts.Read(Scripts.Sample, "t1");

        Assert.Equal("t1/in/placement/0", CommandAddress.Of(line, line.Step("in").Commands[0])?.ToString());
        Assert.Equal("t1/in/time/0", CommandAddress.Of(line, line.Step("in").Commands[1])?.ToString());
        Assert.Equal("t1/in/expression/0", CommandAddress.Of(line, line.Step("in").Commands[2])?.ToString());
    }
}
