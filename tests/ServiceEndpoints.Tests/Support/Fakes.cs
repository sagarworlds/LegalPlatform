using DocumentService.Storage;
using EventBus.Abstractions;
using EventBus.Events;

namespace ServiceEndpoints.Tests.Support;

/// <summary>Records integration events instead of sending them to RabbitMQ.</summary>
public sealed class FakeEventBus : IEventBus
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public IReadOnlyList<T> Of<T>() => Published.OfType<T>().ToList();
}

/// <summary>In-memory <see cref="IDocumentStorage"/>.</summary>
public sealed class FakeDocumentStorage : IDocumentStorage
{
    public Dictionary<string, byte[]> Blobs { get; } = [];

    /// <summary>Makes <see cref="DeleteAsync"/> fail, to exercise the "blob could not be cleaned up" path.</summary>
    public bool FailDeletes { get; set; }

    public Task SaveAsync(string storagePath, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        Blobs[storagePath] = buffer.ToArray();
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (!Blobs.TryGetValue(storagePath, out var bytes))
        {
            throw new DocumentStorageException($"'{storagePath}' does not exist.", new FileNotFoundException(storagePath));
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (FailDeletes)
        {
            throw new DocumentStorageException($"Cannot delete '{storagePath}'.", new IOException("simulated"));
        }

        Blobs.Remove(storagePath);
        return Task.CompletedTask;
    }
}
