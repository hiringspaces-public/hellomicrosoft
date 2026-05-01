
public class CosmosClient : ICosmosClient
{
    private readonly Dictionary<string, CounterDocument> _store = new();

    public async Task<CounterDocument?> ReadAsync(string id, string partitionKey)
    {
        int rand = new Random().Next(1, 100);
        await Task.Delay(rand);  // simulate network latency
        var key = $"{partitionKey}:{id}";
        _store.TryGetValue(key, out var doc);
        return doc;
    }

    public async Task UpsertAsync(CounterDocument document, string? matchETag)
    {
        int rand = new Random().Next(1, 100);
        await Task.Delay(rand);  // simulate network latency
        var key = $"{document.partitionKey}:{document.id}";
        _store[key] = document;
        return;
    }
}
