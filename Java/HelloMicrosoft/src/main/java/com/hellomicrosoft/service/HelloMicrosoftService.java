package com.hellomicrosoft.service;

import com.hellomicrosoft.interfaces.ICounterRepository;
import com.hellomicrosoft.interfaces.IHelloMicrosoftService;
import com.hellomicrosoft.models.HelloResult;
import org.springframework.stereotype.Service;

@Service
public class HelloMicrosoftService implements IHelloMicrosoftService {

    private final ICounterRepository repository;

    public HelloMicrosoftService(ICounterRepository repository) {
        this.repository = repository;
    }

    @Override
    public HelloResult sayHello(String userId) {
        long newCount = repository.increment();

        boolean isWinner = newCount % 10000 == 0;

        HelloResult result = new HelloResult();
        result.setCount(newCount);
        result.setWinner(isWinner);
        result.setMessage(isWinner
                ? "Congrats " + userId + ", you hit " + newCount + "!"
                : "Hello!");

        return result;
    }
}
