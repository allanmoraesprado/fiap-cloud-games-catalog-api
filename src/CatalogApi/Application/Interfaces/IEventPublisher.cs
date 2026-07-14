namespace CatalogApi.Application.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync(string topic, string key, object message, CancellationToken ct = default);
}
