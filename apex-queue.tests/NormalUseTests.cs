using DJ.Codes;
using Xunit;

namespace ApexQueue.Tests;

public class NormalUseTests
{
    [Fact]
    public void Take_ReturnsHighestPriorityItemFirst()
    {
        ApexQueue<string> q = new();
        q.Add("low", 1);
        q.Add("high", 10);
        q.Add("mid", 5);

        Assert.Equal("high", q.Take());
    }

    [Fact]
    public void Take_WithinSamePriority_IsFIFO()
    {
        ApexQueue<string> q = new();
        q.Add("first",  5);
        q.Add("second", 5);
        q.Add("third",  5);

        Assert.Equal("first",  q.Take());
        Assert.Equal("second", q.Take());
        Assert.Equal("third",  q.Take());
    }

    [Fact]
    public void Take_DrainsPriorityLevelsInDescendingOrder()
    {
        ApexQueue<int> q = new();
        q.Add(1, 10);
        q.Add(2, 10);
        q.Add(3, 5);
        q.Add(4, 1);

        Assert.Equal(1, q.Take());
        Assert.Equal(2, q.Take());
        Assert.Equal(3, q.Take());
        Assert.Equal(4, q.Take());
    }

    [Fact]
    public void Count_ReflectsTotalItemsAcrossAllPriorities()
    {
        ApexQueue<int> q = new();
        Assert.Equal(0, q.Count());

        q.Add(1, 5);
        q.Add(2, 5);
        q.Add(3, 10);
        Assert.Equal(3, q.Count());

        q.Take();
        Assert.Equal(2, q.Count());
    }

    [Fact]
    public void MaxPriority_ReflectsHighestNonEmptyPriority()
    {
        ApexQueue<string> q = new();
        q.Add("a", 3);
        q.Add("b", 7);

        Assert.Equal(7, q.MaxPriority);
    }

    [Fact]
    public void MaxPriority_UpdatesWhenHighestQueueDrains()
    {
        ApexQueue<string> q = new();
        q.Add("low",  1);
        q.Add("high", 10);

        q.Take(); // drains priority 10
        Assert.Equal(1, q.MaxPriority);
    }
}
