// =============================================================================
// CosmosCounterRepositoryTests.cs
//
public class CosmosCounterRepositoryTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────
    private static CosmosCounterRepository NewRepo(out CosmosClient store)
    {
        store = new CosmosClient();
        return new CosmosCounterRepository(store);
    }

    // ── Baseline (sequential) ─────────────────────────────────────────────────

    [Fact]
    public async Task Increment_FirstCall_ReturnsOne()
    {
        var repo = NewRepo(out _);

        var result = await repo.IncrementAsync();

        Assert.Equal(1, result);
    }

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


    [Fact]
    public async Task Increment_PersistentConflicts_ThrowsAfterMaxRetries()
    {
        var stub = new AlwaysConflictingCosmosClient();
        var repo = new CosmosCounterRepository(stub);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.IncrementAsync());
    }
}
