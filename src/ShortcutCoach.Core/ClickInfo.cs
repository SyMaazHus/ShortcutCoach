using System.Text.RegularExpressions;

namespace ShortcutCoach.Core;

/// <summary>
/// One resolved mouse click: what was clicked, in which process, and when.
/// </summary>
/// <remarks>
/// A <c>record</c> is like an immutable C struct whose fields are set once at
/// creation; the compiler also generates value-based equality for it.
/// </remarks>
public sealed record ClickInfo(
    string ProcessName,
    string ElementName,
    string ControlType,
    DateTime Timestamp)
{
    private const string UnnamedPlaceholder = "(unnamed)";

    /// <summary>Formats as <c>Bold (Button) — WINWORD</c>.</summary>
    public override string ToString() =>
        $"{DisplayName(ElementName)} ({ControlType}) — {ProcessName}";

    // UIA names can contain newlines or runs of spaces (e.g. multi-line labels);
    // collapse them so each click fits on one log line.
    private static string DisplayName(string name)
    {
        string collapsed = Regex.Replace(name, @"\s+", " ").Trim();
        return collapsed.Length == 0 ? UnnamedPlaceholder : collapsed;
    }
}
