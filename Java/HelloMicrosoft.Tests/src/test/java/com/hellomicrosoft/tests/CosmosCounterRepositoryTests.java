// =============================================================================
// CosmosCounterRepositoryTests.java
// Tests for CosmosCounterRepository
// =============================================================================

package com.hellomicrosoft.tests;
import com.hellomicrosoft.dao.CosmosClient;
import com.hellomicrosoft.dao.CosmosCounterRepository;
import com.hellomicrosoft.interfaces.ICosmosClient;
import com.hellomicrosoft.models.CounterDocument;

import org.junit.jupiter.api.Test;

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

// =============================================================================
// ---------------------------------------------------------------------------
// 1. CosmosCounterRepositoryTests
// ---------------------------------------------------------------------------
// =============================================================================
class CosmosCounterRepositoryTests {

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CosmosCounterRepository newRepo(ICosmosClient[] outClient) {
        ICosmosClient client = new CosmosClient();
        outClient[0] = client;
        return new CosmosCounterRepository(client);
    }

    // ── Baseline (sequential) ─────────────────────────────────────────────────

    @Test
    void increment_FirstCall_ReturnsOne() throws Exception {
        ICosmosClient[] clientRef = new ICosmosClient[1];
        CosmosCounterRepository repo = newRepo(clientRef);

        long result = repo.increment();

        assertEquals(1L, result);
    }

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

    @Test
    void increment_PersistentConflicts_ThrowsAfterMaxRetries() {
        AlwaysConflictingCosmosClient stub = new AlwaysConflictingCosmosClient();
        CosmosCounterRepository repo = new CosmosCounterRepository(stub);

        assertThrows(IllegalStateException.class, repo::increment);
    }
}