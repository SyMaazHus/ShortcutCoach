using ShortcutCoach.Core;

namespace ShortcutCoach.Core.Tests;

public class RollingMaxTests
{
    [Fact]
    public void Max_IsZero_WhenEmpty()
    {
        Assert.Equal(0, new RollingMax(3).Max);
    }

    [Fact]
    public void Max_IsLargestValue_WhenWindowNotFull()
    {
        var max = new RollingMax(5);
        max.Add(4);
        max.Add(9);
        max.Add(2);
        Assert.Equal(9, max.Max);
    }

    [Fact]
    public void Max_ForgetsSpike_AfterCapacityNewerValues()
    {
        var max = new RollingMax(3);
        max.Add(100);
        max.Add(1);
        max.Add(2);
        Assert.Equal(100, max.Max);

        max.Add(3); // evicts the 100
        Assert.Equal(3, max.Max);
    }

    [Fact]
    public void Max_HandlesCapacityOfOne()
    {
        var max = new RollingMax(1);
        max.Add(50);
        max.Add(7);
        Assert.Equal(7, max.Max);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_WhenCapacityBelowOne(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingMax(capacity));
    }
}
