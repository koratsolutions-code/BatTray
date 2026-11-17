using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static BatTray.Core.Hid.HidNative;

namespace BatTray.Core.Hid;

/// <summary>An open handle to one HID collection: feature, input and output report I/O.</summary>
public sealed class HidConnection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream? _stream;

    private HidConnection(HidCollectionInfo collection, SafeFileHandle handle, bool streaming)
    {
        Collection = collection;
        _handle = handle;
        if (streaming)
            _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
    }

    public HidCollectionInfo Collection { get; }

    /// <summary>Opens a collection for feature reports only (no read/write stream).</summary>
    public static HidConnection? OpenForFeatures(HidCollectionInfo collection) =>
        Open(collection, GENERIC_READ | GENERIC_WRITE, streaming: false)
        ?? Open(collection, 0, streaming: false);

    /// <summary>Opens a collection for reading input reports and writing output reports.</summary>
    public static HidConnection? OpenForReports(HidCollectionInfo collection) =>
        Open(collection, GENERIC_READ | GENERIC_WRITE, streaming: true);

    private static HidConnection? Open(HidCollectionInfo collection, uint access, bool streaming)
    {
        var flags = streaming ? FILE_FLAG_OVERLAPPED : 0;
        var handle = CreateFile(collection.Path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new HidConnection(collection, handle, streaming);
    }

    /// <summary>Reads a feature report. <paramref name="buffer"/>[0] must hold the report id.</summary>
    public bool GetFeature(byte[] buffer) => HidD_GetFeature(_handle, buffer, buffer.Length);

    /// <summary>Sends a feature report. <paramref name="buffer"/>[0] holds the report id.</summary>
    public bool SetFeature(byte[] buffer) => HidD_SetFeature(_handle, buffer, buffer.Length);

    /// <summary>Polls an input report. <paramref name="buffer"/>[0] must hold the report id.</summary>
    public bool GetInputReport(byte[] buffer) => HidD_GetInputReport(_handle, buffer, buffer.Length);

    /// <summary>Discards input reports already queued by the driver.</summary>
    public void FlushInput() => HidD_FlushQueue(_handle);

    /// <summary>Extracts a usage value from a report buffer using the collection's descriptor.</summary>
    public bool TryGetUsageValue(HidReportType type, ushort usagePage, ushort usage, byte[] report, out uint value)
    {
        value = 0;
        if (!HidD_GetPreparsedData(_handle, out var preparsed))
            return false;
        try
        {
            return HidP_GetUsageValue((int)type, usagePage, 0, usage, out value, preparsed, report, (uint)report.Length)
                   == HIDP_STATUS_SUCCESS;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>Writes an output report (padded by the caller to the collection's output length).</summary>
    public async Task<bool> WriteAsync(byte[] report, CancellationToken ct)
    {
        if (_stream is null)
            throw new InvalidOperationException("Connection was not opened for reports.");
        try
        {
            await _stream.WriteAsync(report, ct);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Reads the next input report, or returns null when <paramref name="timeout"/> elapses.</summary>
    public async Task<byte[]?> ReadAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (_stream is null)
            throw new InvalidOperationException("Connection was not opened for reports.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var buffer = new byte[Math.Max(1, Collection.InputLength)];
        try
        {
            var read = await _stream.ReadAsync(buffer, cts.Token);
            return read > 0 ? buffer[..read] : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _handle.Dispose();
    }

    public static int LastError => Marshal.GetLastWin32Error();
}
