namespace BatTray.Core.Hid.Protocols;

/// <summary>Shared request/response helpers for vendor HID protocols.</summary>
internal static class HidTransact
{
    /// <summary>
    /// Writes <paramref name="request"/> as an output report (zero-padded to the collection's output length)
    /// and returns the first input report accepted by <paramref name="accept"/>, or null on timeout.
    /// Unrelated reports (notifications, replies to other software) are skipped.
    /// </summary>
    public static async Task<byte[]?> WriteReadAsync(
        HidCollectionInfo collection, byte[] request, Func<byte[], bool> accept, TimeSpan timeout, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        connection.FlushInput();
        if (!await connection.WriteAsync(Pad(request, collection.OutputLength), ct))
            return null;

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (accept(frame))
                return frame;
        }
        return null;
    }

    /// <summary>Reads input reports passively (nothing is sent) until one is accepted or the timeout passes.</summary>
    public static async Task<byte[]?> ListenAsync(
        HidCollectionInfo collection, Func<byte[], bool> accept, TimeSpan timeout, CancellationToken ct)
    {
        using var connection = HidConnection.OpenForReports(collection);
        if (connection is null)
            return null;

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await connection.ReadAsync(deadline - DateTime.UtcNow, ct);
            if (frame is null)
                return null;
            if (accept(frame))
                return frame;
        }
        return null;
    }

    public static byte[] Pad(byte[] data, int length)
    {
        if (data.Length >= length)
            return data;
        var padded = new byte[length];
        data.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>Picks the collection of a USB interface (MI_xx), optionally requiring a usage page.</summary>
    public static HidCollectionInfo? FindInterface(HidReceiver receiver, int interfaceNumber, ushort? usagePage = null, Func<HidCollectionInfo, bool>? extra = null) =>
        receiver.Collections.FirstOrDefault(c =>
            c.InterfaceNumber == interfaceNumber &&
            (usagePage is null || c.UsagePage == usagePage) &&
            c.OutputLength > 0 && c.InputLength > 0 &&
            (extra is null || extra(c)));
}
