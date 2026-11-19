namespace BatTray.Core.Hid.Protocols;

/// <summary>Small helpers shared by the gaming-mouse protocols (voltage curves, feature polling).</summary>
internal static class GamingMouseHelpers
{
    /// <summary>
    /// Linear interpolation of a percentage from a voltage table sorted from the highest to the lowest
    /// voltage. Values outside the table are clamped to its ends.
    /// </summary>
    public static int Interpolate(int millivolts, ReadOnlySpan<int> voltsDescending, ReadOnlySpan<int> percents)
    {
        if (millivolts >= voltsDescending[0])
            return percents[0];
        for (var i = 1; i < voltsDescending.Length; i++)
        {
            if (millivolts < voltsDescending[i])
                continue;
            var hi = voltsDescending[i - 1];
            var lo = voltsDescending[i];
            var span = percents[i - 1] - percents[i];
            return percents[i] + (int)Math.Round((double)(millivolts - lo) * span / (hi - lo), MidpointRounding.AwayFromZero);
        }
        return percents[^1];
    }

    /// <summary>Returns a copy of <paramref name="data"/> zero-padded (or cut) to exactly <paramref name="length"/> bytes.</summary>
    public static byte[] Sized(byte[] data, int length)
    {
        var buffer = new byte[length];
        Array.Copy(data, buffer, Math.Min(data.Length, length));
        return buffer;
    }

    /// <summary>Reads input reports until one is accepted, or returns null once <paramref name="timeout"/> has passed.</summary>
    public static async Task<byte[]?> ReadUntilAsync(
        HidConnection connection, TimeSpan timeout, Func<byte[], bool> accept, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            var frame = await connection.ReadAsync(left, ct);
            if (frame is null)
                return null;
            if (accept(frame))
                return frame;
        }
        return null;
    }

    /// <summary>Writes an output report (padded to the collection's length) and waits for an accepted reply.</summary>
    public static async Task<byte[]?> WriteReadAsync(
        HidConnection connection, byte[] request, TimeSpan timeout, Func<byte[], bool> accept, CancellationToken ct)
    {
        connection.FlushInput();
        if (!await connection.WriteAsync(HidTransact.Pad(request, connection.Collection.OutputLength), ct))
            return null;
        return await ReadUntilAsync(connection, timeout, accept, ct);
    }

    /// <summary>
    /// Sends a feature report, then reads feature report <paramref name="replyId"/> back up to
    /// <paramref name="reads"/> times, <paramref name="delay"/> apart, until <paramref name="accept"/> takes one.
    /// </summary>
    public static async Task<byte[]?> FeatureQueryAsync(
        HidConnection connection, byte[] request, byte replyId, int replyLength,
        TimeSpan delay, int reads, Func<byte[], bool> accept, CancellationToken ct)
    {
        if (!connection.SetFeature(request))
            return null;
        for (var i = 0; i < reads; i++)
        {
            await Task.Delay(delay, ct);
            var reply = new byte[replyLength];
            reply[0] = replyId;
            if (!connection.GetFeature(reply))
                return null;
            if (accept(reply))
                return reply;
        }
        return null;
    }
}
