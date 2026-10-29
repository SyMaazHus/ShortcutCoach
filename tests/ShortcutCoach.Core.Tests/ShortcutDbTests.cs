using ShortcutCoach.Core;

namespace ShortcutCoach.Core.Tests;

public class ShortcutDbTests
{
    // Builds a one-app DB from entry JSON, e.g. Db("""{ "name": "Copy", "shortcut": "Ctrl+C" }""").
    private static ShortcutDb Db(params string[] entries) =>
        ShortcutDb.Parse($$"""{ "version": 1, "apps": { "explorer": [ {{string.Join(",", entries)}} ] } }""");

    // --- The real, embedded database ---

    [Fact]
    public void BuiltIn_LoadsAndValidates()
    {
        // Parse rejects bad shortcuts, unknown control types and duplicates,
        // so loading at all proves every entry is well-formed.
        ShortcutDb db = ShortcutDb.LoadBuiltIn();
        Assert.True(db.Count > 0);
    }

    // Element names and control types below were probed from the real apps on 2026-10-29.
    [Theory]
    [InlineData("explorer", "Copy", "Button", "Ctrl+C")]
    [InlineData("explorer", "Rename", "Button", "F2")]
    [InlineData("explorer", "Details", "Button", "Alt+Shift+P")]          // details pane toggle
    [InlineData("explorer", "Details", "RadioButton", "Ctrl+Shift+6")]    // details view
    [InlineData("explorer", "Folder", "MenuItem", "Ctrl+Shift+N")]
    [InlineData("chrome", "Back", "Button", "Alt+Left")]
    [InlineData("chrome", "New tab", "Button", "Ctrl+T")]
    [InlineData("WINWORD", "Underline", "SplitButton", "Ctrl+U")]
    [InlineData("WINWORD", "Underline", "Button", "Ctrl+U")]
    [InlineData("WINWORD", "Font...", "Button", "Ctrl+D")]
    public void BuiltIn_HasProbedEntries(string process, string name, string controlType, string expected)
    {
        Assert.Equal(expected, ShortcutDb.LoadBuiltIn().Lookup(process, name, controlType));
    }

    [Fact]
    public void BuiltIn_HasNoVsCodeEntries()
    {
        // Deliberately empty until VS Code has been probed.
        Assert.Null(ShortcutDb.LoadBuiltIn().Lookup("Code", "Copy", "MenuItem"));
    }

    // --- Lookup rules ---

    [Fact]
    public void Lookup_IgnoresCaseOfProcessNameAndType()
    {
        ShortcutDb db = Db("""{ "name": "Copy", "controlType": "Button", "shortcut": "Ctrl+C" }""");
        Assert.Equal("Ctrl+C", db.Lookup("EXPLORER", "copy", "button"));
    }

    [Theory]
    [InlineData("  Copy \n")]
    [InlineData("&Copy")]
    [InlineData("Copy...")]
    [InlineData("Copy…")]
    [InlineData("Copy (Ctrl+C)")]
    public void Lookup_NormalizesElementName(string name)
    {
        ShortcutDb db = Db("""{ "name": "Copy", "shortcut": "Ctrl+C" }""");
        Assert.Equal("Ctrl+C", db.Lookup("explorer", name, "Button"));
    }

    [Fact]
    public void Lookup_EntryWithoutControlType_MatchesAnyType()
    {
        ShortcutDb db = Db("""{ "name": "Copy", "shortcut": "Ctrl+C" }""");
        Assert.Equal("Ctrl+C", db.Lookup("explorer", "Copy", "Button"));
        Assert.Equal("Ctrl+C", db.Lookup("explorer", "Copy", "MenuItem"));
    }

    [Fact]
    public void Lookup_EntryWithControlType_MatchesOnlyThatType()
    {
        ShortcutDb db = Db("""{ "name": "Copy", "controlType": "Button", "shortcut": "Ctrl+C" }""");
        Assert.Null(db.Lookup("explorer", "Copy", "ListItem"));
    }

    [Fact]
    public void Lookup_TypedEntryBeatsUntyped()
    {
        ShortcutDb db = Db(
            """{ "name": "Details", "shortcut": "Ctrl+Shift+6" }""",
            """{ "name": "Details", "controlType": "Button", "shortcut": "Alt+Shift+P" }""");

        Assert.Equal("Alt+Shift+P", db.Lookup("explorer", "Details", "Button"));
        Assert.Equal("Ctrl+Shift+6", db.Lookup("explorer", "Details", "RadioButton"));
    }

    [Fact]
    public void Lookup_ReturnsNormalizedShortcut()
    {
        ShortcutDb db = Db("""{ "name": "Copy", "shortcut": "control+c" }""");
        Assert.Equal("Ctrl+C", db.Lookup("explorer", "Copy", "Button"));
    }

    [Theory]
    [InlineData("notepad", "Copy")]   // unknown app
    [InlineData("explorer", "Paste")] // known app, unknown element
    [InlineData("explorer", "")]
    [InlineData("explorer", "   ")]
    public void Lookup_ReturnsNull_WhenNoEntry(string process, string name)
    {
        ShortcutDb db = Db("""{ "name": "Copy", "shortcut": "Ctrl+C" }""");
        Assert.Null(db.Lookup(process, name, "Button"));
    }

    // --- Validation ---

    [Theory]
    [InlineData("""{ "name": "Copy", "shortcut": "c" }""")]                                // mnemonic, not a shortcut
    [InlineData("""{ "name": "Copy", "shortcut": "Ctrl+Banana" }""")]
    [InlineData("""{ "name": "Copy", "controlType": "Buton", "shortcut": "Ctrl+C" }""")]   // typo
    [InlineData("""{ "name": "Copy" }""")]                                                 // missing shortcut
    [InlineData("""{ "shortcut": "Ctrl+C" }""")]                                           // missing name
    [InlineData("""{ "name": "Copy", "shortcut": null }""")]
    [InlineData("""{ "name": "  ", "shortcut": "Ctrl+C" }""")]
    public void Parse_RejectsBadEntry(string entry)
    {
        Assert.Throws<InvalidDataException>(() => Db(entry));
    }

    [Fact]
    public void Parse_RejectsDuplicates_AfterNormalization()
    {
        Assert.Throws<InvalidDataException>(() => Db(
            """{ "name": "Copy", "controlType": "Button", "shortcut": "Ctrl+C" }""",
            """{ "name": "copy...", "controlType": "button", "shortcut": "Ctrl+Insert" }"""));
    }

    [Theory]
    [InlineData("""{ "version": 2, "apps": {} }""")]
    [InlineData("""{ "apps": {} }""")]
    [InlineData("""{ "version": 1, "apps": { "explorer": null } }""")]
    [InlineData("not json")]
    [InlineData("null")]
    public void Parse_RejectsBadFile(string json)
    {
        Assert.Throws<InvalidDataException>(() => ShortcutDb.Parse(json));
    }
}
