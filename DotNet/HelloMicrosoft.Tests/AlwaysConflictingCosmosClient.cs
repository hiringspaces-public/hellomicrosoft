
internal class AlwaysConflictingCosmosClient : ICosmosClient
{
    private int _reads;

    public Task<CounterDocument?> ReadAsync(string id, string partitionKey)
    {
        // Return a valid document on every read so the repository never
        // hits the null-bootstrap path and always proceeds to UpsertAsync.
        _reads++;
        return Task.FromResult<CounterDocument?>(new CounterDocument
        {
            id           = id,
            partitionKey = partitionKey,
            value        = _reads,
            ETag         = Guid.NewGuid().ToString()
        });
    }

    public Task UpsertAsync(CounterDocument document, string? matchETag)
    {
        // Always reject — simulates a permanently contested document.
        throw new CosmosConflictException();
    }

    public Task UpsertAsync(CounterDocument document)
    {
        throw new NotImplementedException();
    }
}
