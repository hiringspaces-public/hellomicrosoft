package com.hellomicrosoft.models;

public class CounterDocument {

    private String id = "global-counter";
    private String partitionKey = "counter";
    private long value;
    private String eTag;

    public String getId() { return id; }
    public void setId(String id) { this.id = id; }

    public String getPartitionKey() { return partitionKey; }
    public void setPartitionKey(String partitionKey) { this.partitionKey = partitionKey; }

    public long getValue() { return value; }
    public void setValue(long value) { this.value = value; }

    public String getETag() { return eTag; }
    public void setETag(String eTag) { this.eTag = eTag; }

    public CounterDocument copy() {
        CounterDocument c = new CounterDocument();
        c.setId(this.id);
        c.setPartitionKey(this.partitionKey);
        c.setValue(this.value);
        c.setETag(this.eTag);
        return c;
    }
}
