public class CounterDocument
{
    public string id { get; set; } = "global-counter";
    public string partitionKey { get; set; } = "counter";
    public long value { get; set; }
    public string? ETag { get; set; }
}
