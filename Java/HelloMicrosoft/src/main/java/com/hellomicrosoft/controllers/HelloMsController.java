package com.hellomicrosoft.controllers;

import com.hellomicrosoft.interfaces.IHelloMicrosoftService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
public class HelloMsController {

    private final IHelloMicrosoftService service;

    public HelloMsController(IHelloMicrosoftService service) {
        this.service = service;
    }

    @GetMapping("/hello")
    public String get() {
        return service.sayHello("user").getMessage();
    }
}
