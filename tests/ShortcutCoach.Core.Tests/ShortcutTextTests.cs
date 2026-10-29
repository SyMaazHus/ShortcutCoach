using ShortcutCoach.Core;

namespace ShortcutCoach.Core.Tests;

// Input strings marked "probe" are real AcceleratorKey / Name values read from
// Notepad, File Explorer, Word and Chrome on 2026-10-29.
public class ShortcutTextTests
{
    [Theory]
    [InlineData("Ctrl+C", "Ctrl+C")]                                       // probe: Notepad Copy
    [InlineData("Shift+F3", "Shift+F3")]                                   // probe: Notepad Find previous
    [InlineData("F3", "F3")]                                               // probe: Notepad Find next
    [InlineData("Del", "Del")]                                             // probe: Notepad Delete
    [InlineData("Alt+Left", "Alt+Left")]                                   // probe: Explorer Back
    [InlineData("Alt+Ctrl+C, Alt+Ctrl+V", "Ctrl+Alt+C, Ctrl+Alt+V")]       // probe: Word Format Painter
    [InlineData("Alt + Up Arrow", "Alt+Up")]                               // probe: Explorer Up
    [InlineData("control+shift+n", "Ctrl+Shift+N")]
    [InlineData("Shift+Ctrl+Alt+Del", "Ctrl+Alt+Shift+Del")]
    [InlineData("Ctrl+Shift+Delete", "Ctrl+Shift+Del")]
    [InlineData("Ctrl+,", "Ctrl+,")]
    [InlineData("Ctrl++", "Ctrl++")]
    [InlineData("Ctrl+Plus", "Ctrl++")]
    [InlineData("  Ctrl+B  ", "Ctrl+B")]
    [InlineData("Esc", "Esc")]
    public void TryNormalize_ProducesCanonicalForm(string raw, string expected)
    {
        Assert.True(ShortcutText.TryNormalize(raw, out string? shortcut));
        Assert.Equal(expected, shortcut);
    }

    [Theory]
    [InlineData("t")]                     // probe: Chrome "New tab" reports its mnemonic
    [InlineData("T")]
    [InlineData("1")]
    [InlineData("Shift+A")]               // just a capital letter
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+C tricks")]         // a tab title, not a shortcut
    [InlineData("Hyper+C")]               // unknown modifier
    [InlineData("Ctrl+Banana")]           // unknown key
    [InlineData("Ctrl+C, t")]             // one bad part spoils the list
    public void TryNormalize_RejectsNonShortcuts(string? raw)
    {
        Assert.False(ShortcutText.TryNormalize(raw, out string? shortcut));
        Assert.Null(shortcut);
    }

    [Theory]
    [InlineData("Bold (Ctrl+B)", "Bold", "Ctrl+B")]                                         // probe: Notepad toolbar
    [InlineData("New tab Ctrl+T", "New tab", "Ctrl+T")]                                     // probe: Chrome menu
    [InlineData("Refresh \"Documents\" (F5)", "Refresh \"Documents\"", "F5")]               // probe: Explorer
    [InlineData("Up to \"User\" (Alt + Up Arrow)", "Up to \"User\"", "Alt+Up")]             // probe: Explorer
    [InlineData("Delete browsing data… Ctrl+Shift+Del", "Delete browsing data…", "Ctrl+Shift+Del")] // probe: Chrome menu
    [InlineData("New Incognito window Ctrl+Shift+N", "New Incognito window", "Ctrl+Shift+N")] // probe: Chrome menu
    [InlineData("Full screen F11", "Full screen", "F11")]
    [InlineData("Delete (Del)", "Delete", "Del")]
    public void FromName_ExtractsTrailingShortcut(string name, string expectedBase, string expectedShortcut)
    {
        Assert.Equal(expectedShortcut, ShortcutText.FromName(name, out string baseName));
        Assert.Equal(expectedBase, baseName);
    }

    [Theory]
    [InlineData("Report (F5).docx")]      // Explorer file name: shortcut not at the end
    [InlineData("Ctrl+C tricks")]         // Chrome tab title: shortcut at the start
    [InlineData("Copy")]
    [InlineData("Notes (draft)")]         // parentheses, but not a shortcut
    [InlineData("History")]
    [InlineData("Delete")]                // bare keys count only inside brackets
    [InlineData("Confirm Delete")]
    [InlineData("Press Enter")]
    [InlineData("Escape")]
    public void FromName_ReturnsNull_WhenNameDoesNotEndWithShortcut(string name)
    {
        Assert.Null(ShortcutText.FromName(name, out string baseName));
        Assert.Equal(name, baseName);
    }

    [Fact]
    public void FromName_TrimsBaseName_WhenNoShortcut()
    {
        Assert.Null(ShortcutText.FromName("  Copy \n", out string baseName));
        Assert.Equal("Copy", baseName);
    }
}
