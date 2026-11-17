namespace BatTray.Core;

/// <summary>Optional interface for providers that can describe what they saw (for support and diagnostics).</summary>
public interface IDiagnosticsSource
{
    /// <summary>Human-readable lines describing the devices found, supported or not.</summary>
    IReadOnlyList<string> DescribeDevices();
}
