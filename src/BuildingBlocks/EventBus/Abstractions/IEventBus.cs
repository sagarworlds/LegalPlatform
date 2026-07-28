using EventBus.Events;

namespace EventBus.Abstractions;

public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent;
}
