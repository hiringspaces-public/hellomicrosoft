public interface ICounterRepository
{
    Task<long> IncrementAsync();
}