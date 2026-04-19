// =============================================================================
// AlwaysConflictingCosmosClient.cs
//
// Test philosophy
// ───────────────
// Before the fix  →  RED tests prove the bug is real and measurable.
// After  the fix  →  ALL tests go GREEN, proving the fix is correct.
//
// Three test classes, ordered from simplest to hardest:
//
//   1. CosmosClientStubTests          — unit-tests the in-memory stub itself.
//   2. CosmosCounterRepositoryTests   — tests the optimistic-concurrency loop.
//   3. HelloMicrosoftServiceTests     — tests winner detection end-to-end.
// =============================================================================


// ---------------------------------------------------------------------------
// 2. CosmosCounterRepositoryTests
//    Tests the ETag retry loop inside CosmosCounterRepository.
//    These are the concurrency correctness tests — they FAIL without the fix
//    and PASS with it.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Stub: always throws CosmosConflictException so we can exercise the retry
// ceiling without needing real concurrency.
// ---------------------------------------------------------------------------
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
