using DJ.Codes;
using Xunit;

namespace ApexQueue.Tests;

public class EdgeCaseTests
{
    [Fact]
    public void Take_EmptyQueue_ReturnsNull()
    {
        var q = new ApexQueue<string>();
        Assert.Null(q.Take());
    }

    [Fact]
    public void Take_EmptyQueue_ReturnsDefaultForValueType()
    {
        var q = new ApexQueue<int>();
        Assert.Equal(0, q.Take());
    }

    [Fact]
    public void Count_EmptyQueue_IsZero()
    {
        var q = new ApexQueue<int>();
        Assert.Equal(0, q.Count());
    }

    [Fact]
    public void MaxPriority_EmptyQueue_IsZero()
    {
        var q = new ApexQueue<string>();
        Assert.Equal(0, q.MaxPriority);
    }

    [Fact]
    public void Take_SingleItem_ReturnsItemAndLeavesQueueEmpty()
    {
        var q = new ApexQueue<string>();
        q.Add("only", 1);

        Assert.Equal("only", q.Take());
        Assert.Equal(0, q.Count());
    }

    [Fact]
    public void Add_SamePriorityManyTimes_AllItemsRetrievable()
    {
        var q = new ApexQueue<int>();
        for (int i = 0; i < 1000; i++) q.Add(i, 5);

        Assert.Equal(1000, q.Count());
        for (int i = 0; i < 1000; i++) Assert.Equal(i, q.Take());
    }
}
