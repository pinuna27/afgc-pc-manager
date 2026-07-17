namespace AFGCPCManager.Core.Output;

public interface IConsumerActionEmitter
{
    ValueTask EmitAsync(ConsumerAction action, CancellationToken cancellationToken = default);
}
