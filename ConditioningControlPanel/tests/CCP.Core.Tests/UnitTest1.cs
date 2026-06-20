using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        Assert.Equal("Conditioning Control Panel", CCPCore.AppName);
    }
}
