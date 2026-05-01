package com.hellomicrosoft.interfaces;

import com.hellomicrosoft.models.CounterDocument;

public interface ICosmosClient {

    CounterDocument read(String id, String partitionKey);

    void upsert(CounterDocument document, String matchETag);
}
