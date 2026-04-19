/// <summary>
/// Thrown by <see cref="ICosmosClient.UpsertAsync"/> when the supplied
/// ETag no longer matches the version stored in Cosmos DB (HTTP 412).
/// This signals an optimistic-concurrency conflict: another writer
/// incremented the counter between our read and our write.
/// The caller should re-read the document and retry the operation.
/// </summary>
public class CosmosConflictException : Exception
{
    public CosmosConflictException()
        : base("Cosmos DB precondition failed: the document was modified by a concurrent writer. Re-read and retry.") { }
}
