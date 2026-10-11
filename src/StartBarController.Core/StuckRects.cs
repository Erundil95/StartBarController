namespace StartBarController.Core;

/// <summary>
/// Reads and edits the binary "Settings" value under Explorer\StuckRects3 (and the
/// per-monitor values under Explorer\MMStuckRects3). Explorer keeps the taskbar's
/// docked edge in byte 12 of that blob and reads it back on startup.
/// </summary>
public static class StuckRects
{
    public const int EdgeOffset = 12;
    public const int MinimumLength = EdgeOffset + 1;

    public static TaskbarEdge? ReadEdge(byte[]? blob)
    {
        if (blob is null || blob.Length < MinimumLength)
        {
            return null;
        }

        byte value = blob[EdgeOffset];
        return Enum.IsDefined(typeof(TaskbarEdge), (int)value) ? (TaskbarEdge)value : null;
    }

    /// <summary>Returns a copy of <paramref name="blob"/> with the edge byte replaced.</summary>
    public static byte[] WithEdge(byte[] blob, TaskbarEdge edge)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (blob.Length < MinimumLength)
        {
            throw new ArgumentException(
                $"StuckRects blob is {blob.Length} bytes; expected at least {MinimumLength}.", nameof(blob));
        }

        byte[] copy = (byte[])blob.Clone();
        copy[EdgeOffset] = (byte)edge;
        return copy;
    }
}
