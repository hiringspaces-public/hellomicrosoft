public class HelloMicrosoftService : IHelloMicrosoftService
{
    private readonly ICounterRepository _repository;

    public HelloMicrosoftService(ICounterRepository repository)
    {
        _repository = repository;
    }

    public async Task<HelloResult> SayHelloAsync(string userId)
    {
        var newCount = await _repository.IncrementAsync();

        var isWinner = newCount % 10000 == 0;

        return new HelloResult
        {
            Count = newCount,
            IsWinner = isWinner,
            Message = isWinner
                ? $"Congrats {userId}, you hit {newCount}!"
                : "Hello!"
        };
    }
}
