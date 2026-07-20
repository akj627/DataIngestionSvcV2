using System.Threading.Channels;

namespace DataIngestion.Svc.Services;

public class IngestionChannel : IIngestionQueue
{
    private readonly Channel<(Guid JobId, string ZipUrl)> _channel =
        Channel.CreateUnbounded<(Guid, string)>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid jobId, string zipUrl, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync((jobId, zipUrl), ct);

    public IAsyncEnumerable<(Guid JobId, string ZipUrl)> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
