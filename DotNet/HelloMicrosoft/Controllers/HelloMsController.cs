using Microsoft.AspNetCore.Mvc;

public class HelloMsController : ControllerBase
{
    private readonly IHelloMicrosoftService service;
    public HelloMsController(IHelloMicrosoftService service)
    {
        this.service = service;
    }

    [HttpGet("/hello")]
    public string Get()
    {
        return service.SayHelloAsync("user").Result.Message;
    }
}