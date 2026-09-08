using System;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class RelayVersionTests
{
    [Theory]
    [InlineData("1.4.3", "1.4.2")]
    [InlineData("1.5.0", "1.4.2")]
    [InlineData("2.0.0", "1.4.2")]
    [InlineData("v1.4.3", "1.4.2")]
    [InlineData("1.4.3+abcdef", "1.4.2")]
    [InlineData("1.4.3", "1.4.2+5500f2e")]
    [InlineData("1.4.3-beta", "1.4.2")]
    public void IsServerNewer_WhenServerIsNewer_ReturnsTrue(string server, string local)
    {
        Assert.True(RelayVersion.IsServerNewer(server, local));
    }

    [Theory]
    [InlineData("1.4.1", "1.4.2")]
    [InlineData("1.4.0", "1.4.2")]
    [InlineData("1.0.0", "1.4.2")]
    [InlineData("1.4.1", "1.4.1")]
    [InlineData("1.4.2", "1.4.2")]
    [InlineData("v1.4.1", "1.4.2")]
    [InlineData("1.4.1", "1.4.2+5500f2e")]
    [InlineData("1.4.1", "1.4.1+5500f2e")]
    [InlineData("1.4.1", "1.4.2-dev")]
    public void IsServerNewer_WhenServerIsOlderOrEqual_ReturnsFalse_PreventingDowngrade(string server, string local)
    {
        Assert.False(RelayVersion.IsServerNewer(server, local));
    }

    [Theory]
    [InlineData(null, "1.4.2")]
    [InlineData("", "1.4.2")]
    [InlineData("   ", "1.4.2")]
    [InlineData("1.4.2", null)]
    [InlineData("1.4.2", "")]
    [InlineData("not-a-version", "1.4.2")]
    [InlineData("1.4.3", "invalid")]
    public void IsServerNewer_MalformedOrNullStrings_ReturnsFalse(string? server, string? local)
    {
        Assert.False(RelayVersion.IsServerNewer(server, local));
    }

    [Theory]
    [InlineData("v1.4.2", "1.4.2")]
    [InlineData("V1.4.2", "1.4.2")]
    [InlineData("1.4.2+build.123", "1.4.2")]
    [InlineData("1.4.2-preview.1", "1.4.2")]
    [InlineData("v1.4.2+sha.abcdef", "1.4.2")]
    public void NormalizeVersionString_StripsPrefixesAndMetadata(string input, string expected)
    {
        Assert.Equal(expected, RelayVersion.NormalizeVersionString(input));
    }

    [Fact]
    public void Current_DoesNotReturn1_0_0()
    {
        RelayVersion.ResetCacheForTesting();
        var current = RelayVersion.Current;
        Assert.False(string.IsNullOrWhiteSpace(current));
        Assert.NotEqual("1.0.0", current);
        Assert.StartsWith("1.4.", current);
    }
}
