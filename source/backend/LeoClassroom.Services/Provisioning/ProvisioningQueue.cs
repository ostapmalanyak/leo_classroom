using System.Threading.Channels;

namespace LeoClassroom.Services.Provisioning;

public interface IProvisioningQueue
{
    public ValueTask EnqueueAsync(long acceptanceId);
    public IAsyncEnumerable<long> DequeueAllAsync(CancellationToken cancellationToken);
}

internal sealed class ProvisioningQueue : IProvisioningQueue
{
    private const int Capacity = 1024;

    private readonly Channel<long> _channel = Channel.CreateBounded<long>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });

    public async ValueTask EnqueueAsync(long acceptanceId) => await _channel.Writer.WriteAsync(acceptanceId);

    public IAsyncEnumerable<long> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
