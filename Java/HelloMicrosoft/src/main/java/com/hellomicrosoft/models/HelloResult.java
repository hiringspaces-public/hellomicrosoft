package com.hellomicrosoft.models;

public class HelloResult {

    private long count;
    private boolean isWinner;
    private String message = "";

    public long getCount() { return count; }
    public void setCount(long count) { this.count = count; }

    public boolean isWinner() { return isWinner; }
    public void setWinner(boolean winner) { this.isWinner = winner; }

    public String getMessage() { return message; }
    public void setMessage(String message) { this.message = message; }
}
