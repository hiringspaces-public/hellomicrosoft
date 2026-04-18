public interface IHelloMicrosoftService
{
    Task<HelloResult> SayHelloAsync(string userId);
}
