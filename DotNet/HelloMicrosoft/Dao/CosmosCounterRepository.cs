public class CosmosCounterRepository : ICounterRepository
{
    private readonly ICosmosClient _cosmosClient;

    private const string Id = "global-counter";
    private const string PartitionKey = "counter";

    public CosmosCounterRepository(ICosmosClient cosmosClient)
    {
        _cosmosClient = cosmosClient;
    }

    public async Task<long> IncrementAsync()
    {
        var doc = await _cosmosClient.ReadAsync(Id, PartitionKey);

        if (doc == null)
        {
            doc = new CounterDocument
            {
                id = Id,
                partitionKey = PartitionKey,
                value = 0
            };
        }

        doc.value += 1;

        await _cosmosClient.UpsertAsync(doc);

        return doc.value;
    }
}
