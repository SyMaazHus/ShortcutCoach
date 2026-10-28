using ShortcutCoach.Core;

namespace ShortcutCoach.Core.Tests;

public class ClickInfoTests
{
    private static ClickInfo Click(string name, string type = "Button", string process = "WINWORD") =>
        new(process, name, type, new DateTime(2026, 1, 1));

    [Fact]
    public void ToString_FormatsNameTypeAndProcess()
    {
        Assert.Equal("Bold (Button) — WINWORD", Click("Bold").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void ToString_UsesPlaceholder_WhenNameIsBlank(string name)
    {
        Assert.Equal("(unnamed) (Button) — WINWORD", Click(name).ToString());
    }

    [Fact]
    public void ToString_CollapsesNewlinesAndRepeatedSpaces()
    {
        Assert.Equal(
            "Save As (MenuItem) — notepad",
            Click("  Save\r\n   As \n", "MenuItem", "notepad").ToString());
    }
}
