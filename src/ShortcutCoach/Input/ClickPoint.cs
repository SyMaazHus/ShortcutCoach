using System.Drawing;

namespace ShortcutCoach.Input;

/// <summary>
/// A click as captured by the hook: where it happened, plus facts that must be
/// read at mouse-down, before the screen changes.
/// </summary>
/// <param name="IsWebContent">True when the window under the point renders Chromium page content.</param>
/// <remarks>
/// A <c>record struct</c> is a value type, copied like a C struct; passing it
/// through the channel allocates nothing.
/// </remarks>
internal readonly record struct ClickPoint(Point Point, bool IsWebContent);
