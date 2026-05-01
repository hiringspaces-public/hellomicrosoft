package com.hellomicrosoft.dao;

import com.hellomicrosoft.interfaces.ICosmosClient;
import com.hellomicrosoft.models.CounterDocument;
import org.springframework.stereotype.Component;

import java.util.concurrent.ConcurrentHashMap;

@Component
public class CosmosClient implements ICosmosClient {

    private final ConcurrentHashMap<String, CounterDocument> store = new ConcurrentHashMap<>();

    @Override
    public CounterDocument read(String id, String partitionKey) {
        int rand = (int)(Math.random() * 100) + 1;
        try { Thread.sleep(rand); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } // simulate network latency
        String key = partitionKey + ":" + id;
        return store.getOrDefault(key, null);
    }

    @Override
    public void upsert(CounterDocument document, String matchETag) {
        int rand = (int)(Math.random() * 100) + 1;
        try { Thread.sleep(rand); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } // simulate network latency
        String key = document.getPartitionKey() + ":" + document.getId();
        store.put(key, document);
    }
}
