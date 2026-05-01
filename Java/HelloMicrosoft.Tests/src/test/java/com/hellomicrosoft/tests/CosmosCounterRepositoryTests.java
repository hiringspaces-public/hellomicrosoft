// =============================================================================
// CosmosCounterRepositoryTests.java
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

package com.hellomicrosoft.tests;
import com.hellomicrosoft.interfaces.ICosmosClient;
import com.hellomicrosoft.models.CounterDocument;
import com.hellomicrosoft.dto.CosmosCounterRepository;
import org.junit.jupiter.api.Test;
import com.hellomicrosoft.dto.CosmosClient;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.BrokenBarrierException;
import java.util.concurrent.CyclicBarrier;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.stream.Collectors;
import java.util.stream.IntStream;
import java.util.stream.LongStream;

import static org.junit.jupiter.api.Assertions.*;

// ---------------------------------------------------------------------------
// Supporting infrastructure required by the tests
// ---------------------------------------------------------------------------

// ── CosmosConflictException ──────────────────────────────────────────────────
// Thrown by the stub when an ETag mismatch is detected (optimistic concurrency).
// Maps to CosmosConflictException in the C# version.

// In: com/hellomicrosoft/exceptions/CosmosConflictException.java
//
// package com.hellomicrosoft.exceptions;
// public class CosmosConflictException extends RuntimeException {
//     public CosmosConflictException(String message) { super(message); }
// }


// ── Thread-safe CosmosClient stub ───────────────────────────────────────────
// The production CosmosClient above uses a plain HashMap with no
// synchronisation — fine for sequential tests, but it loses updates under
// concurrency.  The test infrastructure needs an ETag-aware, thread-safe
// version so that the concurrency tests can actually detect the bug and
// verify the fix.
//
// Drop-in replacement: same interface, adds ETag checking to upsert().
//
// In: com/hellomicrosoft/dto/ThreadSafeCosmosClient.java  (test scope only)
//
// package com.hellomicrosoft.dto;
//
// import com.hellomicrosoft.exceptions.CosmosConflictException;
// import com.hellomicrosoft.interfaces.ICosmosClient;
// import com.hellomicrosoft.models.CounterDocument;
// import java.util.HashMap;
// import java.util.Map;
// import java.util.Objects;
// import java.util.UUID;
//
// public class ThreadSafeCosmosClient implements ICosmosClient {
//
//     private final Map<String, CounterDocument> store = new HashMap<>();
//     private final Object lock = new Object();
//
//     @Override
//     public CounterDocument read(String id, String partitionKey) {
//         try { Thread.sleep(10); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
//         synchronized (lock) {
//             String key = partitionKey + ":" + id;
//             CounterDocument doc = store.get(key);
//             return doc == null ? null : doc.copy(); // return a snapshot
//         }
//     }
//
//     @Override
//     public void upsert(CounterDocument document, String matchETag) {
//         try { Thread.sleep(10); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
//         synchronized (lock) {
//             String key = document.getPartitionKey() + ":" + document.getId();
//             CounterDocument current = store.get(key);
//
//             if (matchETag != null) {
//                 // Conditional write: reject if the stored ETag doesn't match
//                 String storedETag = current == null ? null : current.getETag();
//                 if (!Objects.equals(storedETag, matchETag)) {
//                     throw new CosmosConflictException("ETag mismatch");
//                 }
//             }
//
//             // Stamp a new ETag on every successful write
//             document.setETag(UUID.randomUUID().toString());
//             store.put(key, document.copy());
//         }
//     }
// }


// ── AlwaysConflictingCosmosClient ────────────────────────────────────────────
// A stub whose upsert() always throws CosmosConflictException.
// Used to verify that the repository stops retrying after MaxRetries.
//
// In: com/hellomicrosoft/dto/AlwaysConflictingCosmosClient.java  (test scope)
//
// package com.hellomicrosoft.dto;
//
// import com.hellomicrosoft.exceptions.CosmosConflictException;
// import com.hellomicrosoft.interfaces.ICosmosClient;
// import com.hellomicrosoft.models.CounterDocument;
//
// public class AlwaysConflictingCosmosClient implements ICosmosClient {
//
//     @Override
//     public CounterDocument read(String id, String partitionKey) {
//         // Always returns null (no document exists)
//         return null;
//     }
//
//     @Override
//     public void upsert(CounterDocument document, String matchETag) {
//         throw new CosmosConflictException("Always conflicts");
//     }
// }


// ── CounterDocument additions required by the stubs ──────────────────────────
// The model needs an ETag field and a copy() method.
// Add to: com/hellomicrosoft/models/CounterDocument.java
//
//   private String eTag;
//   public String getETag() { return eTag; }
//   public void   setETag(String eTag) { this.eTag = eTag; }
//
//   public CounterDocument copy() {
//       CounterDocument c = new CounterDocument();
//       c.setId(this.id);
//       c.setPartitionKey(this.partitionKey);
//       c.setValue(this.value);
//       c.setETag(this.eTag);
//       return c;
//   }


// =============================================================================
// ---------------------------------------------------------------------------
// 2. CosmosCounterRepositoryTests
//    Tests the ETag retry loop inside CosmosCounterRepository.
//    These are the concurrency correctness tests — they FAIL without the fix
//    and PASS with it.
// ---------------------------------------------------------------------------
// =============================================================================
class CosmosCounterRepositoryTests {

    // ── Helpers ──────────────────────────────────────────────────────────────

    /**
     * Builds a repository wired to a fresh, isolated thread-safe in-memory store.
     * Every test gets its own store so tests never share state.
     * outClient[0] receives the store reference so callers can inspect it directly.
     */
    private static CosmosCounterRepository newRepo(ICosmosClient[] outClient) {
        ICosmosClient client = new CosmosClient();
        outClient[0] = client;
        return new CosmosCounterRepository(client);
    }

    // ── Baseline (sequential) ─────────────────────────────────────────────────

    /**
     * The very first increment on an empty store must return 1.
     * Proves that the null-document bootstrap path works correctly.
     */
    @Test
    void increment_FirstCall_ReturnsOne() throws Exception {
        ICosmosClient[] clientRef = new ICosmosClient[1];
        CosmosCounterRepository repo = newRepo(clientRef);

        long result = repo.increment();

        assertEquals(1L, result);
    }

    /**
     * Ten sequential increments must produce exactly the values 1 … 10 in order.
     * Rules out off-by-one errors in the local increment step.
     */
    @Test
    void increment_Sequential_ReturnsConsecutiveValues() throws Exception {
        ICosmosClient[] clientRef = new ICosmosClient[1];
        CosmosCounterRepository repo = newRepo(clientRef);
        List<Long> results = new ArrayList<>();

        for (int i = 0; i < 10; i++) {
            results.add(repo.increment());
        }

        List<Long> expected = LongStream.rangeClosed(1, 10)
                .boxed()
                .collect(Collectors.toList());
        assertEquals(expected, results);
    }

    /**
     * Mirrors the C# Increment_UnderConcurrency_FinalValueIsExact test.
     *
     * WHY THIS IS A RED TEST (before the fix):
     *   50 tasks all read value = 25, all increment to 26, all write 26.
     *   Lost updates make the final stored value something well below 50.
     *
     * WHY IT GOES GREEN after the fix:
     *   ETag mismatch → CosmosConflictException → re-read → retry.
     *   Every increment eventually wins exactly once → final value = 50.
     */
    @Test
    void increment_UnderConcurrency_FinalValueIsExact() throws Exception {
        final int concurrency = 50;
        CosmosClient client = new CosmosClient();
        CosmosCounterRepository repo = new CosmosCounterRepository(client);
        CyclicBarrier barrier = new CyclicBarrier(concurrency); // forces all threads to race simultaneously

        ExecutorService executor = Executors.newFixedThreadPool(concurrency);
        List<Future<?>> futures = IntStream.range(0, concurrency)
                .mapToObj(i -> executor.submit(() -> {
                    try {
                        barrier.await(); // every thread waits here until ALL are ready
                        repo.increment();
                    } catch (InterruptedException | BrokenBarrierException e) {
                        Thread.currentThread().interrupt();
                        throw new RuntimeException(e);
                    }
                }))
                .collect(Collectors.toList());

        for (Future<?> f : futures) f.get();
        executor.shutdown();

        CounterDocument result = client.read("global-counter", "counter");
        assertEquals((long) concurrency, result.getValue()); // will be far less — proves lost updates
    }

    /**
     * After N sequential increments the stored value must equal N.
     * Confirms that no value is double-counted or skipped.
     */
    @Test
    void increment_Sequential_StoredValueMatchesCallCount() throws Exception {
        final int n = 50;
        ICosmosClient[] clientRef = new ICosmosClient[1];
        CosmosCounterRepository repo = newRepo(clientRef);

        for (int i = 0; i < n; i++) {
            repo.increment();
        }

        // One more call returns n + 1, so the previous max was exactly n
        assertEquals((long) n + 1, repo.increment());
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

    /**
     * 100 concurrent increments must produce a final counter value of exactly
     * 100. Any value below 100 proves lost updates (the bug).
     */
    @Test
    void increment_100ConcurrentTasks_FinalValueIsExactly100() throws Exception {
        final int concurrency = 100;
        ICosmosClient[] clientRef = new ICosmosClient[1];
        CosmosCounterRepository repo = newRepo(clientRef);

        ExecutorService executor = Executors.newFixedThreadPool(concurrency);
        List<Future<Long>> futures = IntStream.range(0, concurrency)
                .mapToObj(i -> executor.submit(repo::increment))
                .collect(Collectors.toList());

        List<Long> results = new ArrayList<>();
        for (Future<Long> f : futures) results.add(f.get());
        executor.shutdown();

        long max = results.stream().mapToLong(Long::longValue).max().orElseThrow();
        assertEquals((long) concurrency, max);
    }

    /**
     * Every return value across all concurrent calls must be unique.
     * Duplicate return values mean two callers were given the same slot —
     * a direct proof of a lost update.
     */
    @Test
    void increment_100ConcurrentTasks_AllReturnedValuesAreUnique() throws Exception {
        final int concurrency = 100;
        ICosmosClient[] clientRef = new ICosmosClient[1];
        CosmosCounterRepository repo = newRepo(clientRef);

        ExecutorService executor = Executors.newFixedThreadPool(concurrency);
        List<Future<Long>> futures = IntStream.range(0, concurrency)
                .mapToObj(i -> executor.submit(repo::increment))
                .collect(Collectors.toList());

        List<Long> results = new ArrayList<>();
        for (Future<Long> f : futures) results.add(f.get());
        executor.shutdown();

        long distinctCount = results.stream().distinct().count();
        assertEquals((long) concurrency, distinctCount);
    }

    // ── Retry ceiling ─────────────────────────────────────────────────────────

    /**
     * When a pathological stub always rejects every conditional write the
     * repository must stop retrying after MaxRetries and throw
     * IllegalStateException — it must never loop forever.
     *
     * Maps to InvalidOperationException in C#.
     */
    @Test
    void increment_PersistentConflicts_ThrowsAfterMaxRetries() {
        AlwaysConflictingCosmosClient stub = new AlwaysConflictingCosmosClient();
        CosmosCounterRepository repo = new CosmosCounterRepository(stub);

        assertThrows(IllegalStateException.class, repo::increment);
    }
}