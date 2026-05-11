using DJ.Codes;
using Xunit;

namespace ApexQueue.Tests;

public class EdgeCaseTests
{
    [Fact]
    public void Take_EmptyQueue_ReturnsNull()
    {
        ApexQueue<string> q = new();
        Assert.Null(q.Take());
    }

    [Fact]
    public void Take_EmptyQueue_ReturnsDefaultForValueType()
    {
        ApexQueue<int> q = new();
        Assert.Equal(0, q.Take());
    }

    [Fact]
    public void Count_EmptyQueue_IsZero()
    {
        ApexQueue<int> q = new();
        Assert.Equal(0, q.Count());
    }

    [Fact]
    public void MaxPriority_EmptyQueue_IsZero()
    {
        ApexQueue<string> q = new();
        Assert.Equal(0, q.MaxPriority);
    }

    [Fact]
    public void Take_SingleItem_ReturnsItemAndLeavesQueueEmpty()
    {
        ApexQueue<string> q = new();
        q.Add("only", 1);

        Assert.Equal("only", q.Take());
        Assert.Equal(0, q.Count());
    }

    [Fact]
    public void Add_SamePriorityManyTimes_AllItemsRetrievable()
    {
        ApexQueue<int> q = new();
        for (int i = 0; i < 1000; i++) q.Add(i, 5);

        Assert.Equal(1000, q.Count());
        for (int i = 0; i < 1000; i++) Assert.Equal(i, q.Take());
    }
}
