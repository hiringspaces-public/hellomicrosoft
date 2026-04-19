public interface ICosmosClient
{
    Task<CounterDocument?> ReadAsync(string id, string partitionKey);
    Task UpsertAsync(CounterDocument document, string? matchETag = null);
}
