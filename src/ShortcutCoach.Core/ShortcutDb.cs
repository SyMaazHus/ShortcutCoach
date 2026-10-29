using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShortcutCoach.Core;

/// <summary>
/// Fallback shortcuts for elements whose app doesn't expose one through UI
/// Automation, keyed by process name + element name + (optional) control type.
/// </summary>
/// <remarks>
/// Matching ignores case, extra whitespace, a trailing shortcut ("Bold (Ctrl+B)"),
/// <c>&amp;</c> mnemonic markers and a trailing ellipsis ("Font..."). An entry with a
/// control type beats one without, which means "any control type except content
/// items" (ListItem, TreeItem, TabItem, DataItem).
/// </remarks>
public sealed class ShortcutDb
{
    private const int SupportedVersion = 1;
    private const string AnyControlType = "";
    private const string BuiltInResource = "ShortcutCoach.Core.shortcuts.json";

    // FlaUI's ControlType names, which is what ClickInfo.ControlType holds.
    // A typo in the JSON would otherwise never match and fail silently.
    private static readonly HashSet<string> KnownControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown", "AppBar", "Button", "Calendar", "CheckBox", "ComboBox", "Custom", "DataGrid",
        "DataItem", "Document", "Edit", "Group", "Header", "HeaderItem", "Hyperlink", "Image",
        "List", "ListItem", "MenuBar", "Menu", "MenuItem", "Pane", "ProgressBar", "RadioButton",
        "ScrollBar", "SemanticZoom", "Separator", "Slider", "Spinner", "SplitButton", "StatusBar",
        "Tab", "TabItem", "Table", "Text", "Thumb", "TitleBar", "ToolBar", "ToolTip", "Tree",
        "TreeItem", "Window",
    };

    private static readonly HashSet<string> ContentControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ListItem", "TreeItem", "TabItem", "DataItem",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Missing "name"/"shortcut" or explicit nulls become errors instead of nulls.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // A value tuple is like an anonymous C struct; it compares field by field,
    // so it works as a composite dictionary key. All parts are upper-cased.
    private readonly Dictionary<(string Process, string Name, string ControlType), string> _entries;

    private ShortcutDb(Dictionary<(string, string, string), string> entries) => _entries = entries;

    /// <summary>Number of entries across all apps.</summary>
    public int Count => _entries.Count;

    /// <summary>Loads the database compiled into this assembly.</summary>
    public static ShortcutDb LoadBuiltIn()
    {
        using Stream stream = typeof(ShortcutDb).Assembly.GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException($"Embedded resource '{BuiltInResource}' is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Parses and validates a database. Throws <see cref="InvalidDataException"/>
    /// on malformed JSON, an unknown control type, a shortcut that doesn't parse,
    /// or a duplicate entry.
    /// </summary>
    public static ShortcutDb Parse(string json)
    {
        DbFile file;
        try
        {
            file = JsonSerializer.Deserialize<DbFile>(json, JsonOptions)
                ?? throw new InvalidDataException("Shortcut DB is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Shortcut DB is not valid: {ex.Message}", ex);
        }

        if (file.Version != SupportedVersion)
            throw new InvalidDataException($"Shortcut DB version {file.Version} is not supported (expected {SupportedVersion}).");

        var entries = new Dictionary<(string, string, string), string>();
        foreach (var (process, list) in file.Apps)
        {
            if (string.IsNullOrWhiteSpace(process) || list is null)
                throw new InvalidDataException($"Shortcut DB has an empty app name or entry list ('{process}').");

            foreach (DbEntry entry in list)
            {
                string where = $"{process} / {entry.Name}";

                if (!ShortcutText.TryNormalize(entry.Shortcut, out string? shortcut))
                    throw new InvalidDataException($"Shortcut DB: '{entry.Shortcut}' is not a valid shortcut ({where}).");

                string controlType = entry.ControlType ?? AnyControlType;
                if (controlType != AnyControlType && !KnownControlTypes.Contains(controlType))
                    throw new InvalidDataException($"Shortcut DB: unknown control type '{controlType}' ({where}).");

                string name = NormalizeName(entry.Name);
                if (name.Length == 0)
                    throw new InvalidDataException($"Shortcut DB: empty element name ({process}).");

                if (!entries.TryAdd(Key(process, name, controlType), shortcut))
                    throw new InvalidDataException($"Shortcut DB: duplicate entry ({where}, {controlType}).");
            }
        }

        return new ShortcutDb(entries);
    }

    /// <summary>Returns the canonical shortcut for the element, or null if the DB has none.</summary>
    public string? Lookup(string processName, string elementName, string controlType)
    {
        string name = NormalizeName(elementName);
        if (name.Length == 0)
            return null;

        if (_entries.TryGetValue(Key(processName, name, controlType), out string? shortcut))
            return shortcut;

        // Content items are named by the user (a file called "Copy", a tab titled
        // "Paste"), so only an entry that names their type explicitly may match them.
        if (ContentControlTypes.Contains(controlType))
            return null;

        return _entries.GetValueOrDefault(Key(processName, name, AnyControlType));
    }

    private static (string, string, string) Key(string process, string name, string controlType) =>
        (process.Trim().ToUpperInvariant(), name.ToUpperInvariant(), controlType.Trim().ToUpperInvariant());

    private static string NormalizeName(string name)
    {
        ShortcutText.FromName(name, out string baseName);
        baseName = baseName.Replace("&", "");
        baseName = Regex.Replace(baseName, @"(\.\.\.|…)$", "");
        return Regex.Replace(baseName, @"\s+", " ").Trim();
    }

    // Shapes of the JSON file. Records with positional parameters are like
    // immutable C structs; System.Text.Json fills them through their constructor.
    private sealed record DbFile(int Version, Dictionary<string, List<DbEntry>> Apps);

    // The default value is what makes "controlType" optional in the JSON.
    private sealed record DbEntry(string Name, string Shortcut, string? ControlType = null);
}
