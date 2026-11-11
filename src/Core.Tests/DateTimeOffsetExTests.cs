using System;
using Xunit;

namespace GitCredentialManager.Tests;

public class DateTimeOffsetExTests
{
    [Theory]
    [InlineData("981163506", 981163506L)]
    [InlineData("0", 0L)]
    [InlineData("-62135596800", -62135596800L)] // DateTimeOffset.MinValue
    [InlineData("253402300799", 253402300799L)] // DateTimeOffset.MaxValue
    [InlineData("-62135596801", null)]
    [InlineData("253402300800", null)]
    [InlineData("9223372036854775807", null)]
    [InlineData("-9223372036854775808", null)]
    [InlineData("1.5", null)]
    [InlineData("invalid", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DateTimeOffsetEx_TryParseUnixTimeSeconds(string str, long? expected)
    {
        bool result = DateTimeOffsetEx.TryParseUnixTimeSeconds(str, out DateTimeOffset value);

        Assert.Equal(expected.HasValue, result);
        if (expected.HasValue)
        {
            Assert.Equal(expected.Value, value.ToUnixTimeSeconds());
        }
    }
}
