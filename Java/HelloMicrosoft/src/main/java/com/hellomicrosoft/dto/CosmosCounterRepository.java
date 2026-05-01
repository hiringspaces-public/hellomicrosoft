package com.hellomicrosoft.dto;

import com.hellomicrosoft.interfaces.ICosmosClient;
import com.hellomicrosoft.interfaces.ICounterRepository;
import com.hellomicrosoft.models.CounterDocument;
import org.springframework.stereotype.Component;

@Component
public class CosmosCounterRepository implements ICounterRepository {

    private final ICosmosClient cosmosClient;

    private static final String ID            = "global-counter";
    private static final String PARTITION_KEY = "counter";

    public CosmosCounterRepository(ICosmosClient cosmosClient) {
        this.cosmosClient = cosmosClient;
    }

    @Override
    public long increment() {
        CounterDocument doc = cosmosClient.read(ID, PARTITION_KEY);

        if (doc == null) {
            doc = new CounterDocument();
            doc.setId(ID);
            doc.setPartitionKey(PARTITION_KEY);
            doc.setValue(0);
        }

        doc.setValue(doc.getValue() + 1);

        cosmosClient.upsert(doc, null);

        return doc.getValue();
    }
}
