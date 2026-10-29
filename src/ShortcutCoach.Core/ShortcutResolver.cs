using System.Text.RegularExpressions;

namespace ShortcutCoach.Core;

/// <summary>Where a suggested shortcut came from.</summary>
public enum ShortcutSource
{
    /// <summary>The element's UIA AcceleratorKey.</summary>
    Accelerator,

    /// <summary>Parsed from the end of the element's name, e.g. "Bold (Ctrl+B)".</summary>
    Name,

    /// <summary>The fallback <see cref="ShortcutDb"/>.</summary>
    Database,
}

/// <summary>A shortcut to suggest for a clicked element.</summary>
/// <param name="ElementName">Display name, without any shortcut text it contained.</param>
/// <param name="Shortcut">Canonical shortcut, e.g. <c>Ctrl+C</c>.</param>
public sealed record Suggestion(string ElementName, string Shortcut, ShortcutSource Source);

/// <summary>
/// Finds the keyboard shortcut for a click, trying in order:
/// 1. the AcceleratorKey (if it's a real shortcut, not a menu mnemonic);
/// 2. a shortcut at the end of the name (MenuItem, Button and SplitButton only);
/// 3. the fallback DB.
/// Inside web pages only tier 1 is used: page authors choose element names,
/// so a page button named "Back" or "Save (Ctrl+S)" proves nothing.
/// </summary>
public sealed class ShortcutResolver
{
    // Other control types (list items, tabs, links) carry user content such as
    // file names or page titles, where "(F5)" at the end is just text.
    private static readonly HashSet<string> NameTierControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MenuItem", "Button", "SplitButton",
    };

    private readonly ShortcutDb _db;

    public ShortcutResolver(ShortcutDb db) => _db = db;

    /// <summary>Returns the suggestion for this click, or null if there's no shortcut to suggest.</summary>
    public Suggestion? Resolve(ClickInfo click)
    {
        string name = Regex.Replace(click.ElementName, @"\s+", " ").Trim();

        // Nothing to call the element in the toast, so nothing to coach.
        if (name.Length == 0)
            return null;

        // Strip "(Ctrl+B)" from "Bold (Ctrl+B)" so the toast doesn't repeat it,
        // whichever tier wins. A name that's only a shortcut keeps its full text.
        string? fromName = null;
        string displayName = name;
        if (NameTierControlTypes.Contains(click.ControlType))
        {
            fromName = ShortcutText.FromName(name, out string baseName);
            if (fromName != null && baseName.Length > 0)
                displayName = baseName;
            else
                fromName = null;
        }

        if (ShortcutText.TryNormalize(click.AcceleratorKey, out string? accelerator))
            return new Suggestion(displayName, accelerator, ShortcutSource.Accelerator);

        if (click.IsWebContent)
            return null;

        if (fromName != null)
            return new Suggestion(displayName, fromName, ShortcutSource.Name);

        // "x is { } y" means "if x is not null, call it y", like
        // `if ((y = x) != NULL)` in C.
        if (_db.Lookup(click.ProcessName, name, click.ControlType) is { } fromDb)
            return new Suggestion(displayName, fromDb, ShortcutSource.Database);

        return null;
    }
}
