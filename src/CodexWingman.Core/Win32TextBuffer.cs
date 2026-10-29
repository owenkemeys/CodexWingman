namespace CodexWingman.Core;

/// <summary>Reads Win32 text whose length isn't known up front, such as a process image path (long paths included).</summary>
public static class Win32TextBuffer
{
    public const int MaxChars = 32768;

    /// <summary>
    /// Calls <paramref name="read"/> with <paramref name="initial"/> characters, doubling (up to <see cref="MaxChars"/>)
    /// while it reports the buffer too small. Null when it fails or is still too small at the limit.
    /// </summary>
    public static string? Read(int initial, Func<int, (string? Value, bool TooSmall)> read)
    {
        for (var capacity = Math.Clamp(initial, 1, MaxChars); ; capacity = Math.Min(capacity * 2, MaxChars))
        {
            var (value, tooSmall) = read(capacity);
            if (!tooSmall) return value;
            if (capacity >= MaxChars) return null;
        }
    }
}
