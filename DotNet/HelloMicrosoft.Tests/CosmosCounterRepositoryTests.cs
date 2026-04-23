// =============================================================================
// CosmosCounterRepositoryTests.cs
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
public class CosmosCounterRepositoryTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a repository wired to a fresh, isolated in-memory store.
    /// Every test gets its own store so tests never share state.
    /// </summary>
    private static CosmosCounterRepository NewRepo(out CosmosClient store)
    {
        store = new CosmosClient();
        return new CosmosCounterRepository(store);
    }

    // ── Baseline (sequential) ─────────────────────────────────────────────────

    /// <summary>
    /// The very first increment on an empty store must return 1.
    /// Proves that the null-document bootstrap path works correctly.
    /// </summary>
    [Fact]
    public async Task Increment_FirstCall_ReturnsOne()
    {
        var repo = NewRepo(out _);

        var result = await repo.IncrementAsync();

        Assert.Equal(1, result);
    }

    /// <summary>
    /// Ten sequential increments must produce exactly the values 1 … 10 in order.
    /// Rules out off-by-one errors in the local increment step.
    /// </summary>
    [Fact]
    public async Task Increment_Sequential_ReturnsConsecutiveValues()
    {
        var repo    = NewRepo(out _);
        var results = new List<long>();

        for (int i = 0; i < 10; i++)
            results.Add(await repo.IncrementAsync());

        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), results);
    }

    [Fact]
    public async Task Increment_UnderConcurrency_FinalValueIsExact()
    {
        const int concurrency = 50;
        var client = new CosmosClient();
        var repo = new CosmosCounterRepository(client);
        var barrier = new Barrier(concurrency); // forces all threads to race simultaneously

        var tasks = Enumerable
            .Range(0, concurrency)
            .Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait(); // every thread waits here until ALL are ready
                await repo.IncrementAsync();
            }));

        await Task.WhenAll(tasks);

        var result = await client.ReadAsync("global-counter", "counter");
        Assert.Equal(concurrency, result!.value); // will be far less — proves lost updates
    }

    /// <summary>
    /// After N sequential increments the stored value must equal N.
    /// Confirms that no value is double-counted or skipped.
    /// </summary>
    [Fact]
    public async Task Increment_Sequential_StoredValueMatchesCallCount()
    {
        const int n = 50;
        var repo    = NewRepo(out _);

        for (int i = 0; i < n; i++)
            await repo.IncrementAsync();

        // One more call returns n + 1, so the previous max was exactly n
        Assert.Equal(n + 1, await repo.IncrementAsync());
    }

    // ── Test 1 — Atomicity and Concurrency ───────────────────────────────────
    //
    // WHY 100 THREADS?
    // ─────────────────
    // At low concurrency (e.g. 2 threads) the broken code may accidentally
    // pass — thread scheduling may never open a race window.  At 100 threads
    // the probability of at least one lost update is effectively 1.
    // "Why not 2?" is itself a good interview question.
    //
    // WHY IT FAILS WITHOUT THE FIX:
    //   100 tasks all read value = 50
    //   All increment locally → 51
    //   All write 51 — lost updates make the final value something like 67
    //
    // WHY IT PASSES WITH THE FIX:
    //   ETag mismatch → CosmosConflictException → re-read → retry
    //   Every increment eventually wins exactly once
    //   Final value is exactly 100

    /// <summary>
    /// 100 concurrent increments must produce a final counter value of exactly
    /// 100. Any value below 100 proves lost updates (the bug).
    /// </summary>
    [Fact]
    public async Task Increment_100ConcurrentTasks_FinalValueIsExactly100()
    {
        const int concurrency = 100;
        var repo              = NewRepo(out _);

        var tasks = Enumerable
            .Range(0, concurrency)
            .Select(_ => repo.IncrementAsync());

        var results = await Task.WhenAll(tasks);

        Assert.Equal(concurrency, results.Max());
    }

    /// <summary>
    /// Every return value across all concurrent calls must be unique.
    /// Duplicate return values mean two callers were given the same slot —
    /// a direct proof of a lost update.
    /// </summary>
    [Fact]
    public async Task Increment_100ConcurrentTasks_AllReturnedValuesAreUnique()
    {
        const int concurrency = 100;
        var repo              = NewRepo(out _);

        var tasks = Enumerable
            .Range(0, concurrency)
            .Select(_ => repo.IncrementAsync());

        var results = await Task.WhenAll(tasks);

        Assert.Equal(concurrency, results.Distinct().Count());
    }


    // ── Retry ceiling ─────────────────────────────────────────────────────────

    /// <summary>
    /// When a pathological stub always rejects every conditional write the
    /// repository must stop retrying after MaxRetries and throw
    /// InvalidOperationException — it must never loop forever.
    /// </summary>
    [Fact]
    public async Task Increment_PersistentConflicts_ThrowsAfterMaxRetries()
    {
        var stub = new AlwaysConflictingCosmosClient();
        var repo = new CosmosCounterRepository(stub);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.IncrementAsync());
    }
}
