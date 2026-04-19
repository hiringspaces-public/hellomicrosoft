// =============================================================================
// HelloMicrosoftTests.cs
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

// ---------------------------------------------------------------------------
// 3. HelloMicrosoftServiceTests
//    Tests winner-detection logic in HelloMicrosoftService.
//    Uses a real CosmosCounterRepository + CosmosClient stub so the full
//    call stack is exercised end-to-end.
//
// WHY WINNER DETECTION IS THE HARDEST TEST:
//   Without the fix, multiple threads can all read value = 9 999 and all
//   write 10 000 — giving 3, 5, 8 winners instead of exactly 1.  With the
//   fix only one write of 10 000 is ever accepted by the store, so exactly
//   one caller receives IsWinner = true.
// ---------------------------------------------------------------------------
public class HelloMicrosoftServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full service stack wired to a fresh in-memory store.
    /// </summary>
    private static HelloMicrosoftService NewService()
    {
        var store = new CosmosClient();
        var repo  = new CosmosCounterRepository(store);
        return new HelloMicrosoftService(repo);
    }

    // ── Basic contract ────────────────────────────────────────────────────────

    /// <summary>
    /// The first SayHelloAsync call must return Count = 1 and IsWinner = false.
    /// Confirms the happy path: a regular non-winner response.
    /// </summary>
    [Fact]
    public async Task SayHello_FirstCall_ReturnsCountOneNotWinner()
    {
        var service = NewService();

        var result = await service.SayHelloAsync("alice");

        Assert.Equal(1,       result.Count);
        Assert.False(result.IsWinner);
        Assert.Equal("Hello!", result.Message);
    }

    /// <summary>
    /// Each sequential call must increment Count by exactly 1.
    /// Rules out off-by-one errors in the service layer.
    /// </summary>
    [Fact]
    public async Task SayHello_Sequential_CountIncrementsBy1EachTime()
    {
        var service = NewService();

        for (int expected = 1; expected <= 10; expected++)
        {
            var result = await service.SayHelloAsync("alice");
            Assert.Equal(expected, result.Count);
        }
    }

    // ── Non-winner message ────────────────────────────────────────────────────

    /// <summary>
    /// Any count that is not a multiple of 10 000 must return the plain
    /// "Hello!" message and IsWinner = false.
    /// </summary>
    [Fact]
    public async Task SayHello_RegularCount_ReturnsHelloMessageNotWinner()
    {
        var service = NewService();

        var result = await service.SayHelloAsync("bob");

        Assert.False(result.IsWinner);
        Assert.Equal("Hello!", result.Message);
    }

    // ── Test 2 — Winner Detection Correctness ─────────────────────────────────
    //
    // WHY IT FAILS WITHOUT THE FIX:
    //   Multiple threads read value = 9 999 simultaneously
    //   All increment to 10 000 locally → all see IsWinner = true
    //   You get 3, 5, 8 winners instead of exactly 1
    //   Or worse: the real 10 000th write gets overwritten and nobody wins
    //
    // WHY IT PASSES WITH THE FIX:
    //   Only one write of 10 000 is ever accepted by the store
    //   Exactly one caller gets IsWinner = true
    //   All other callers at the boundary retry to a value above 10 000

    /// <summary>
    /// Among 10 000 concurrent SayHelloAsync calls exactly one must return
    /// IsWinner = true. Zero winners means the real hit was overwritten;
    /// more than one means the ETag guard is missing.
    /// </summary>
    [Fact]
    public async Task SayHello_10000ConcurrentCalls_ExactlyOneWinner()
    {
        const int totalCalls = 10_000;
        var service          = NewService();

        var tasks = Enumerable
            .Range(0, totalCalls)
            .Select(_ => service.SayHelloAsync("user"));

        var results     = await Task.WhenAll(tasks);
        var winnerCount = results.Count(r => r.IsWinner);

        Assert.Equal(1, winnerCount);
    }

    /// <summary>
    /// The single winning result must have Count = 10 000 and carry the
    /// personalised congratulations message containing the user ID and count.
    /// Proves that the service reads the value the repository committed —
    /// not a locally computed value that was later overwritten.
    /// </summary>
    [Fact]
    public async Task SayHello_10000ConcurrentCalls_WinnerHasCorrectCountAndMessage()
    {
        const int totalCalls = 10_000;
        var service          = NewService();

        var tasks = Enumerable
            .Range(0, totalCalls)
            .Select(_ => service.SayHelloAsync("alice"));

        var results = await Task.WhenAll(tasks);
        var winner  = results.Single(r => r.IsWinner);   // throws if 0 or >1

        Assert.Equal(10_000,   winner.Count);
        Assert.Contains("alice",  winner.Message);
        Assert.Contains("10000",  winner.Message);
    }

    /// <summary>
    /// All non-winning results must carry the plain "Hello!" message.
    /// Confirms the service does not accidentally mark intermediate counts
    /// as winners (e.g. via an off-by-one in the modulo check).
    /// </summary>
    [Fact]
    public async Task SayHello_10000ConcurrentCalls_AllNonWinnersReturnHelloMessage()
    {
        const int totalCalls = 10_000;
        var service          = NewService();

        var tasks = Enumerable
            .Range(0, totalCalls)
            .Select(_ => service.SayHelloAsync("user"));

        var results    = await Task.WhenAll(tasks);
        var nonWinners = results.Where(r => !r.IsWinner);

        Assert.All(nonWinners, r => Assert.Equal("Hello!", r.Message));
    }

    // ── Test 3 — Global Counter Consistency (single process) ──────────────────
    //
    // The CosmosClient stub is a singleton within a process — all service
    // calls share the same in-memory store, modelling a single global database.
    // Multi-instance consistency (3 deployed pods) cannot be verified locally
    // without real infrastructure and is left as a senior discussion point:
    // "These tests pass. Does that mean the system works correctly at 3 instances?
    //  Why not?"
    //
    // WHY IT FAILS WITHOUT THE FIX:
    //   Concurrent writes race without an ETag guard
    //   The counter drifts below N (lost updates)
    //
    // WHY IT PASSES WITH THE FIX:
    //   Every increment eventually succeeds exactly once
    //   The full set of returned values is {1 … N} with no gaps or duplicates

    /// <summary>
    /// Five concurrent "clients" each call SayHelloAsync 200 times.
    /// The global counter must reach exactly 1 000 — no duplicates, no gaps
    /// in the full set of returned Count values.
    /// </summary>
    [Fact]
    public async Task SayHello_5ClientsEach200Calls_GlobalCounterReaches1000()
    {
        const int clients        = 5;
        const int callsEach      = 200;
        const int totalExpected  = clients * callsEach;
        var service              = NewService();

        // Each "client" fires its calls sequentially (preserving client-level
        // ordering), while all 5 clients run concurrently against the shared store.
        var clientTasks = Enumerable
            .Range(0, clients)
            .Select(async _ =>
            {
                var batch = new List<HelloResult>();
                for (int i = 0; i < callsEach; i++)
                    batch.Add(await service.SayHelloAsync("user"));
                return batch;
            });

        var allBatches = await Task.WhenAll(clientTasks);
        var allCounts  = allBatches
            .SelectMany(b => b)
            .Select(r => r.Count)
            .ToList();

        // Every slot 1 … 1 000 must appear exactly once
        Assert.Equal(totalExpected, allCounts.Count);
        Assert.Equal(totalExpected, allCounts.Max());
        Assert.Equal(totalExpected, allCounts.Distinct().Count());
    }

    /// <summary>
    /// No Count value returned across all concurrent calls may appear more
    /// than once. A duplicate means two callers were assigned the same slot —
    /// a direct proof of a lost update at the service level.
    /// </summary>
    [Fact]
    public async Task SayHello_ConcurrentCalls_AllCountValuesAreUnique()
    {
        const int concurrency = 500;
        var service           = NewService();

        var tasks = Enumerable
            .Range(0, concurrency)
            .Select(_ => service.SayHelloAsync("user"));

        var results = await Task.WhenAll(tasks);
        var counts  = results.Select(r => r.Count).ToList();

        Assert.Equal(concurrency, counts.Distinct().Count());
    }
}
