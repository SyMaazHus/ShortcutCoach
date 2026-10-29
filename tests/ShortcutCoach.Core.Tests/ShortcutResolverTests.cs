using ShortcutCoach.Core;

namespace ShortcutCoach.Core.Tests;

public class ShortcutResolverTests
{
    // A test-local DB, so these tests don't depend on the shipped entries.
    private static readonly ShortcutResolver Resolver = new(ShortcutDb.Parse("""
        { "version": 1, "apps": {
            "explorer": [ { "name": "Copy", "shortcut": "Ctrl+C" },
                          { "name": "Rename", "shortcut": "F2" } ],
            "chrome":   [ { "name": "Back", "controlType": "Button", "shortcut": "Alt+Left" },
                          { "name": "Save", "controlType": "Button", "shortcut": "Ctrl+Shift+S" } ]
        } }
        """));

    private static ClickInfo Click(
        string process, string name, string type, string accelerator = "", bool web = false) =>
        new(process, name, type, new DateTime(2026, 1, 1), accelerator, web);

    private static void AssertSuggestion(
        Suggestion? actual, string name, string shortcut, ShortcutSource source)
    {
        Assert.NotNull(actual);
        Assert.Equal(new Suggestion(name, shortcut, source), actual);
    }

    // --- Tier 1: AcceleratorKey ---

    [Fact]
    public void Accelerator_IsUsed_WhenValid()
    {
        // probe: Notepad Edit > Copy
        AssertSuggestion(Resolver.Resolve(Click("Notepad", "Copy", "MenuItem", "Ctrl+C")),
            "Copy", "Ctrl+C", ShortcutSource.Accelerator);
    }

    [Fact]
    public void Accelerator_IsNormalized()
    {
        // probe: Word Format Painter
        AssertSuggestion(Resolver.Resolve(Click("WINWORD", "Format Painter", "Button", "Alt+Ctrl+C, Alt+Ctrl+V")),
            "Format Painter", "Ctrl+Alt+C, Ctrl+Alt+V", ShortcutSource.Accelerator);
    }

    [Fact]
    public void Accelerator_BeatsNameAndDb()
    {
        // The DB says Ctrl+C and the name says Ctrl+Ins, but the app's own value wins.
        AssertSuggestion(Resolver.Resolve(Click("explorer", "Copy (Ctrl+Insert)", "Button", "Ctrl+X")),
            "Copy", "Ctrl+X", ShortcutSource.Accelerator);
    }

    [Fact]
    public void Accelerator_DisplayNameDropsShortcutText()
    {
        // probe: Explorer Refresh has both AcceleratorKey and "(F5)" in its name.
        AssertSuggestion(Resolver.Resolve(Click("explorer", "Refresh \"Documents\" (F5)", "Button", "F5")),
            "Refresh \"Documents\"", "F5", ShortcutSource.Accelerator);
    }

    // --- Tier 2: shortcut in the name ---

    [Fact]
    public void Mnemonic_FallsThroughToName()
    {
        // probe: Chrome's menu reports the mnemonic "t" as AcceleratorKey.
        AssertSuggestion(Resolver.Resolve(Click("chrome", "New tab Ctrl+T", "MenuItem", "t")),
            "New tab", "Ctrl+T", ShortcutSource.Name);
    }

    [Theory]
    [InlineData("MenuItem")]
    [InlineData("Button")]
    [InlineData("SplitButton")]
    public void Name_IsUsed_ForCommandControlTypes(string type)
    {
        // probe: Notepad toolbar
        AssertSuggestion(Resolver.Resolve(Click("Notepad", "Bold (Ctrl+B)", type)),
            "Bold", "Ctrl+B", ShortcutSource.Name);
    }

    [Fact]
    public void Name_BeatsDb()
    {
        AssertSuggestion(Resolver.Resolve(Click("explorer", "Copy (Ctrl+Insert)", "Button")),
            "Copy", "Ctrl+Ins", ShortcutSource.Name);
    }

    [Theory]
    [InlineData("explorer", "Report (F5).docx", "ListItem")]  // file name, shortcut not at end
    [InlineData("chrome", "Ctrl+C tricks", "TabItem")]        // tab title, shortcut at start
    [InlineData("explorer", "Report (F5)", "ListItem")]       // shortcut at end, but a ListItem
    [InlineData("chrome", "Tips Ctrl+C", "TabItem")]          // shortcut at end, but a TabItem
    public void Name_FalsePositives_GiveNoSuggestion(string process, string name, string type)
    {
        Assert.Null(Resolver.Resolve(Click(process, name, type)));
    }

    // --- Tier 3: DB ---

    [Fact]
    public void Db_IsUsed_WhenNoAcceleratorOrName()
    {
        // probe: Explorer's command-bar Copy has no AcceleratorKey.
        AssertSuggestion(Resolver.Resolve(Click("explorer", "Copy", "Button")),
            "Copy", "Ctrl+C", ShortcutSource.Database);
    }

    [Fact]
    public void Db_UntypedEntry_MatchesCommandTypesBeyondTier2()
    {
        // Tier 2 only reads MenuItem/Button/SplitButton names; an untyped DB
        // entry also covers e.g. a CheckBox.
        AssertSuggestion(Resolver.Resolve(Click("explorer", "Rename", "CheckBox")),
            "Rename", "F2", ShortcutSource.Database);
    }

    [Fact]
    public void Db_BuiltIn_FileNamedCopy_GivesNoSuggestion()
    {
        var resolver = new ShortcutResolver(ShortcutDb.LoadBuiltIn());
        Assert.Null(resolver.Resolve(Click("explorer", "Copy", "ListItem")));
    }

    // --- Web content: tier 1 only ---

    [Theory]
    [InlineData("Back", "Alt+Left", ShortcutSource.Database)]
    [InlineData("Save (Ctrl+S)", "Ctrl+S", ShortcutSource.Name)]
    public void WebContent_SkipsNameAndDb(string name, string nativeShortcut, ShortcutSource nativeSource)
    {
        // Outside the page (Chrome's toolbar) the same element matches...
        Suggestion? native = Resolver.Resolve(Click("chrome", name, "Button"));
        Assert.NotNull(native);
        Assert.Equal((nativeShortcut, nativeSource), (native.Shortcut, native.Source));

        // ...but a page button with that name gets nothing.
        Assert.Null(Resolver.Resolve(Click("chrome", name, "Button", web: true)));
    }

    [Fact]
    public void WebContent_StillUsesAccelerator()
    {
        AssertSuggestion(Resolver.Resolve(Click("chrome", "Search", "Edit", "Ctrl+L", web: true)),
            "Search", "Ctrl+L", ShortcutSource.Accelerator);
    }

    [Fact]
    public void WebContent_StillRejectsMnemonic()
    {
        Assert.Null(Resolver.Resolve(Click("chrome", "New tab Ctrl+T", "MenuItem", "t", web: true)));
    }

    // --- No suggestion ---

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void BlankName_GivesNoSuggestion(string name)
    {
        Assert.Null(Resolver.Resolve(Click("Notepad", name, "Button", "Ctrl+C")));
    }

    [Fact]
    public void NameThatIsOnlyAShortcut_KeepsFullTextAndIsNotTier2()
    {
        // "(Ctrl+B)" alone would leave an empty display name; don't treat it as tier 2.
        Assert.Null(Resolver.Resolve(Click("Notepad", "(Ctrl+B)", "Button")));
    }

    [Theory]
    [InlineData("Notepad", "Readme.txt", "TabItem")]
    [InlineData("explorer", "Paste", "Button")]   // known app, not in DB
    [InlineData("WINWORD", "Copy", "Button")]     // DB entry belongs to another app
    public void NoMatch_GivesNoSuggestion(string process, string name, string type)
    {
        Assert.Null(Resolver.Resolve(Click(process, name, type)));
    }
}
