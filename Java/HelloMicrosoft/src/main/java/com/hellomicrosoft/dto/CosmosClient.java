package com.hellomicrosoft.dto;

import com.hellomicrosoft.interfaces.ICosmosClient;
import com.hellomicrosoft.models.CounterDocument;
import org.springframework.stereotype.Component;

import java.util.concurrent.ConcurrentHashMap;

@Component
public class CosmosClient implements ICosmosClient {

    private final ConcurrentHashMap<String, CounterDocument> store = new ConcurrentHashMap<>();

    @Override
    public CounterDocument read(String id, String partitionKey) {
        try { Thread.sleep(10); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } // simulate network latency
        String key = partitionKey + ":" + id;
        return store.getOrDefault(key, null);
    }

    @Override
    public void upsert(CounterDocument document, String matchETag) {
        try { Thread.sleep(10); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } // simulate network latency
        String key = document.getPartitionKey() + ":" + document.getId();
        store.put(key, document);
    }
}
