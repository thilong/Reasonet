using Xunit;

namespace Reasonet.Core.Tests;

public class TestProgram
{
    [Fact]
    public void SanityCheck_OnePlusOne_EqualsTwo()
    {
        Assert.Equal(2, 1 + 1);
    }

    [Fact]
    public void SanityCheck_StringConcat_Works()
    {
        var result = "Hello" + ", " + "World!";
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void SanityCheck_ListCount_Works()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.Equal(3, list.Count);
    }
}
