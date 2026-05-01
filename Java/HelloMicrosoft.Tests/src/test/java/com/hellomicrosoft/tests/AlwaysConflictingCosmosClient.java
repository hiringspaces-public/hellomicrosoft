package com.hellomicrosoft.tests;

import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;

import com.hellomicrosoft.interfaces.ICosmosClient;
import com.hellomicrosoft.models.CosmosConflictException;
import com.hellomicrosoft.models.CounterDocument;

public class AlwaysConflictingCosmosClient implements ICosmosClient {
    private final AtomicInteger reads = new AtomicInteger(0);

    @Override
    public CounterDocument read(String id, String partitionKey) {
        int readCount = reads.incrementAndGet();
        CounterDocument doc = new CounterDocument();
        doc.setId(id);
        doc.setPartitionKey(partitionKey);
        doc.setValue(readCount);
        doc.setETag(UUID.randomUUID().toString());
        return doc;
    }

    @Override
    public void upsert(CounterDocument document, String matchETag) {
        throw new CosmosConflictException();
    }
}
