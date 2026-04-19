
public class CosmosClient : ICosmosClient
{
    private readonly Dictionary<string, CounterDocument> _store = new();

    public Task<CounterDocument?> ReadAsync(string id, string partitionKey)
    {
        var key = $"{partitionKey}:{id}";
        _store.TryGetValue(key, out var doc);
        return Task.FromResult(doc);
    }

    public Task UpsertAsync(CounterDocument document, string? matchETag)
    {
        var key = $"{document.partitionKey}:{document.id}";
        _store[key] = document;
        return Task.CompletedTask;
    }
}
