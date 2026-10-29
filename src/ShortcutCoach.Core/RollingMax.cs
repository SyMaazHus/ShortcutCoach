namespace ShortcutCoach.Core;

/// <summary>
/// Largest of the last <c>capacity</c> values added. Not thread-safe:
/// one thread adds and reads; publish <see cref="Max"/> yourself if others need it.
/// </summary>
public sealed class RollingMax
{
    // Ring buffer, like a fixed C array with a wrap-around write index.
    private readonly long[] _values;
    private int _next;
    private int _count;

    public RollingMax(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _values = new long[capacity];
    }

    public int Capacity => _values.Length;

    /// <summary>Largest value in the window, or 0 if nothing has been added.</summary>
    public long Max { get; private set; }

    public void Add(long value)
    {
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        if (_count < _values.Length)
            _count++;

        // Rescan instead of tracking the max incrementally: the evicted value
        // might have been the max. Fine for small windows (~100).
        long max = _values[0];
        for (int i = 1; i < _count; i++)
            if (_values[i] > max)
                max = _values[i];
        Max = max;
    }
}
