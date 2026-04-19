// =============================================================================
// CosmosClientStubTests.cs
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
// 1. CosmosClientStubTests
//    Verifies that the in-memory CosmosClient stub behaves like real Cosmos DB
//    before a single line of repository or service code is involved.
// ---------------------------------------------------------------------------
public class CosmosClientStubTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns a fresh, isolated stub for each test.</summary>
    private static CosmosClient NewStore() => new();

    private static CounterDocument DocWith(long value, string? etag = null) =>
        new() { id = "global-counter", partitionKey = "counter", value = value, ETag = etag };

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reading from an empty store must return null — the repository uses this
    /// to detect the very first write and initialise value = 0.
    /// </summary>
    [Fact]
    public async Task Read_EmptyStore_ReturnsNull()
    {
        var store = NewStore();

        var result = await store.ReadAsync("global-counter", "counter");

        Assert.Null(result);
    }

    /// <summary>
    /// After one upsert the stored value must round-trip back exactly.
    /// </summary>
    [Fact]
    public async Task Read_AfterUpsert_ReturnsStoredValue()
    {
        var store = NewStore();
        var doc   = DocWith(42);
        await store.UpsertAsync(doc, matchETag: null);

        var result = await store.ReadAsync("global-counter", "counter");

        Assert.NotNull(result);
        Assert.Equal(42, result!.value);
    }

    /// <summary>
    /// ReadAsync must return a copy, not the stored reference.
    /// Mutating the returned object must not affect subsequent reads —
    /// this mirrors how the real Cosmos SDK deserialises a fresh object
    /// from the HTTP response body.
    /// </summary>
    [Fact]
    public async Task Read_ReturnsCopy_MutatingItDoesNotAffectStore()
    {
        var store = NewStore();
        await store.UpsertAsync(DocWith(10), matchETag: null);

        var copy = await store.ReadAsync("global-counter", "counter");
        copy!.value = 999;   // mutate the returned copy

        var fresh = await store.ReadAsync("global-counter", "counter");
        Assert.Equal(10, fresh!.value);  // store must be unchanged
    }

    // ── Upsert (unconditional) ────────────────────────────────────────────────

    /// <summary>
    /// An unconditional upsert (matchETag = null) on an empty store must
    /// succeed and assign a non-null ETag — the ETag is needed for the
    /// next conditional write.
    /// </summary>
    [Fact]
    public async Task Upsert_Unconditional_AssignsETag()
    {
        var store = NewStore();
        var doc   = DocWith(1);

        await store.UpsertAsync(doc, matchETag: null);

        var stored = await store.ReadAsync("global-counter", "counter");
        Assert.NotNull(stored!.ETag);
        Assert.NotEmpty(stored.ETag!);
    }

    /// <summary>
    /// Each successful write must rotate the ETag so that stale If-Match
    /// headers are always rejected on the next attempt.
    /// </summary>
    [Fact]
    public async Task Upsert_EachWriteRotatesETag()
    {
        var store = NewStore();

        await store.UpsertAsync(DocWith(1), matchETag: null);
        var first = (await store.ReadAsync("global-counter", "counter"))!.ETag;

        var doc2 = await store.ReadAsync("global-counter", "counter");
        await store.UpsertAsync(doc2!, matchETag: doc2!.ETag);
        var second = (await store.ReadAsync("global-counter", "counter"))!.ETag;

        Assert.NotEqual(first, second);
    }

    // ── Upsert (conditional / ETag check) ────────────────────────────────────

    /// <summary>
    /// A conditional write whose ETag matches the stored version must succeed.
    /// This is the happy path of optimistic concurrency.
    /// </summary>
    [Fact]
    public async Task Upsert_MatchingETag_Succeeds()
    {
        var store = NewStore();
        await store.UpsertAsync(DocWith(1), matchETag: null);

        var doc = await store.ReadAsync("global-counter", "counter");

        // Should not throw
        await store.UpsertAsync(doc!, matchETag: doc!.ETag);

        var stored = await store.ReadAsync("global-counter", "counter");
        Assert.Equal(doc.value, stored!.value);
    }

    /// <summary>
    /// A conditional write with a stale ETag must throw CosmosConflictException.
    /// This is the 412 Precondition-Failed path that the retry loop depends on.
    /// </summary>
    [Fact]
    public async Task Upsert_StaleETag_ThrowsCosmosConflictException()
    {
        var store = NewStore();
        await store.UpsertAsync(DocWith(1), matchETag: null);

        var snap = await store.ReadAsync("global-counter", "counter");  // ETag = v1

        // A second writer commits first, rotating the stored ETag to v2
        var winner = await store.ReadAsync("global-counter", "counter");
        await store.UpsertAsync(winner!, matchETag: winner!.ETag);

        // Original snap now holds a stale ETag — must be rejected
        await Assert.ThrowsAsync<CosmosConflictException>(
            () => store.UpsertAsync(snap!, matchETag: snap!.ETag));
    }

    /// <summary>
    /// A completely wrong ETag (e.g. a fabricated string) must also be rejected.
    /// Guards against callers that pass arbitrary strings instead of the real ETag.
    /// </summary>
    [Fact]
    public async Task Upsert_WrongETag_ThrowsCosmosConflictException()
    {
        var store = NewStore();
        await store.UpsertAsync(DocWith(1), matchETag: null);

        var doc = DocWith(2);

        await Assert.ThrowsAsync<CosmosConflictException>(
            () => store.UpsertAsync(doc, matchETag: "not-a-real-etag"));
    }
}
