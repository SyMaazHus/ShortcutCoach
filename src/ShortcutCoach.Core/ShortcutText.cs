using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ShortcutCoach.Core;

/// <summary>
/// Parses the shortcut strings that apps expose through UI Automation, which
/// come in many spellings (<c>Alt+Ctrl+C</c>, <c>Control+C</c>, <c>Alt + Up Arrow</c>),
/// into one canonical form: modifiers in the order Ctrl, Alt, Shift, Win, then the key.
/// </summary>
public static class ShortcutText
{
    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift", "Win"];

    // Like a C lookup table of string pairs; keys compare case-insensitively.
    private static readonly Dictionary<string, string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = "Ctrl", ["Control"] = "Ctrl", ["Ctl"] = "Ctrl",
        ["Alt"] = "Alt",
        ["Shift"] = "Shift",
        ["Win"] = "Win", ["Windows"] = "Win",
    };

    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Del"] = "Del", ["Delete"] = "Del",
        ["Esc"] = "Esc", ["Escape"] = "Esc",
        ["Enter"] = "Enter", ["Return"] = "Enter",
        ["Tab"] = "Tab",
        ["Space"] = "Space", ["Spacebar"] = "Space",
        ["Backspace"] = "Backspace",
        ["Ins"] = "Ins", ["Insert"] = "Ins",
        ["Home"] = "Home",
        ["End"] = "End",
        ["PgUp"] = "PgUp", ["Page Up"] = "PgUp", ["PageUp"] = "PgUp",
        ["PgDn"] = "PgDn", ["Page Down"] = "PgDn", ["PageDown"] = "PgDn",
        ["Left"] = "Left", ["Left Arrow"] = "Left",
        ["Right"] = "Right", ["Right Arrow"] = "Right",
        ["Up"] = "Up", ["Up Arrow"] = "Up",
        ["Down"] = "Down", ["Down Arrow"] = "Down",
        ["Plus"] = "+",
        ["Minus"] = "-",
    };

    // "Bold (Ctrl+B)", "Refresh "Documents" (F5)": a parenthesised group that ends the name.
    private static readonly Regex ParenSuffix = new(@"^(?<base>.*?)\s*\((?<keys>[^()]+)\)\s*$", RegexOptions.Singleline);

    // "New tab Ctrl+T", "Delete browsing data… Ctrl+Shift+Del": a modifier combo
    // (or a bare F-key) as the last word of the name.
    private static readonly Regex BareSuffix = new(
        @"^(?<base>.*?)\s+(?<keys>(?:(?:Ctrl|Control|Alt|Shift|Win)\+)+\S+|F\d{1,2})\s*$",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Converts <paramref name="raw"/> to canonical form, e.g. <c>Alt+Ctrl+C</c> → <c>Ctrl+Alt+C</c>.
    /// Several shortcuts separated by ", " are each normalized. Returns false for
    /// anything that isn't a usable shortcut, including a bare letter like <c>t</c>
    /// (a menu mnemonic, as Chrome reports it), which only works while the menu is open.
    /// </summary>
    /// <remarks>
    /// An <c>out</c> parameter is a second return value, like passing a pointer in C.
    /// [NotNullWhen(true)] tells the compiler it's non-null whenever this returns true.
    /// </remarks>
    public static bool TryNormalize(string? raw, [NotNullWhen(true)] out string? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Split only on comma + space, so a comma key ("Ctrl+,") survives.
        var parts = new List<string>();
        foreach (string part in Regex.Split(raw.Trim(), @",\s+"))
        {
            if (!TryNormalizeOne(part, out string? one))
                return false;
            parts.Add(one);
        }

        shortcut = string.Join(", ", parts);
        return true;
    }

    /// <summary>
    /// Finds a shortcut at the very end of an element name and returns it normalized,
    /// or null if there is none. <paramref name="baseName"/> receives the name
    /// without the shortcut (or the whole trimmed name when null is returned).
    /// </summary>
    /// <remarks>
    /// Only the end of the name counts, so "Ctrl+C tricks" or "Report (F5).docx"
    /// don't match.
    /// </remarks>
    public static string? FromName(string name, out string baseName)
    {
        baseName = name.Trim();

        foreach (Regex pattern in (Regex[])[ParenSuffix, BareSuffix])
        {
            Match match = pattern.Match(baseName);
            if (match.Success && TryNormalize(match.Groups["keys"].Value, out string? shortcut))
            {
                baseName = match.Groups["base"].Value.Trim();
                return shortcut;
            }
        }

        return null;
    }

    private static bool TryNormalizeOne(string part, [NotNullWhen(true)] out string? shortcut)
    {
        shortcut = null;
        part = part.Trim();

        // "Ctrl++" means Ctrl and the plus key; splitting on '+' would lose it.
        string keyText;
        string[] modifierTexts;
        if (part.Length > 1 && part.EndsWith('+') && part.TrimEnd('+').Length < part.Length - 1)
        {
            keyText = "+";
            modifierTexts = SplitOnPlus(part[..^1].TrimEnd().TrimEnd('+'));
        }
        else
        {
            string[] tokens = SplitOnPlus(part);
            keyText = tokens[^1];
            modifierTexts = tokens[..^1];
        }

        var modifiers = new HashSet<string>();
        foreach (string text in modifierTexts)
        {
            if (!Modifiers.TryGetValue(text, out string? modifier))
                return false;
            modifiers.Add(modifier);
        }

        if (!TryNormalizeKey(keyText, out string? key, out bool isNamed))
            return false;

        // A printable key alone or with only Shift just types a character:
        // that's a mnemonic or plain typing, not a shortcut.
        if (!isNamed && (modifiers.Count == 0 || modifiers.SetEquals(["Shift"])))
            return false;

        // LINQ's Where filters a sequence, like a for loop that keeps matching items.
        var ordered = ModifierOrder.Where(modifiers.Contains).Append(key);
        shortcut = string.Join("+", ordered);
        return true;
    }

    private static string[] SplitOnPlus(string text) =>
        text.Length == 0 ? [] : text.Split('+', StringSplitOptions.TrimEntries);

    /// <param name="isNamed">True for keys that are shortcuts on their own (F-keys, Del, arrows…).</param>
    private static bool TryNormalizeKey(string text, [NotNullWhen(true)] out string? key, out bool isNamed)
    {
        key = null;
        isNamed = false;
        text = Regex.Replace(text.Trim(), @"\s+", " ");

        if (text.Length == 1 && !char.IsWhiteSpace(text[0]))
        {
            key = char.ToUpperInvariant(text[0]).ToString();
            return true;
        }

        if (Regex.IsMatch(text, @"^F([1-9]|1\d|2[0-4])$", RegexOptions.IgnoreCase))
        {
            key = text.ToUpperInvariant();
            isNamed = true;
            return true;
        }

        if (NamedKeys.TryGetValue(text, out key))
        {
            // "Plus"/"Minus" stand for printable characters.
            isNamed = key.Length > 1;
            return true;
        }

        return false;
    }
}
