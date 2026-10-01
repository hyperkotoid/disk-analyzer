namespace DiskUsage.Core;

public interface IElevatedScanHost
{
    Task<IScanConnection> StartAsync(CancellationToken cancellationToken);
}

public interface IScanConnection : IAsyncDisposable
{
    IAsyncEnumerable<ScanMessage> ReadAsync(CancellationToken cancellationToken);
    Task SendAsync(ScanMessage message, CancellationToken cancellationToken);
}
