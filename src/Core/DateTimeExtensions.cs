using System;

namespace GitCredentialManager;

public static class DateTimeOffsetEx
{
    /// <summary>
    /// Try to parse a 64-bit integer as a Unix time in seconds, returning true if successful and false otherwise.
    /// </summary>
    /// <param name="seconds">Unix timestamp in seconds.</param>
    /// <param name="value">Parsed DateTimeOffset value.</param>
    /// <returns>True if parsing was successful, false otherwise</returns>
    public static bool TryParseUnixTimeSeconds(long seconds, out DateTimeOffset value)
    {
        if (seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() &&
            seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Try to parse a string as a Unix time in seconds, returning true if successful and false otherwise.
    /// </summary>
    /// <param name="str">String of Unix timestamp in seconds.</param>
    /// <param name="value">Parsed DateTimeOffset value.</param>
    /// <returns>True if parsing was successful, false otherwise</returns>
    public static bool TryParseUnixTimeSeconds(string str, out DateTimeOffset value)
    {
        if (long.TryParse(str, out long seconds) && TryParseUnixTimeSeconds(seconds, out value))
        {
            return true;
        }

        value = default;
        return false;
    }


    /// <summary>
    /// Try to parse a 64-bit integer as a Unix time in milliseconds, returning true if successful and false otherwise.
    /// </summary>
    /// <param name="milliseconds">Unix timestamp in milliseconds.</param>
    /// <param name="value">Parsed DateTimeOffset value.</param>
    /// <returns>True if parsing was successful, false otherwise</returns>
    public static bool TryParseUnixTimeMilliseconds(long milliseconds, out DateTimeOffset value)
    {
        if (milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() &&
            milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Try to parse a string as a Unix time in milliseconds, returning true if successful and false otherwise.
    /// </summary>
    /// <param name="str">String of Unix timestamp in milliseconds.</param>
    /// <param name="value">Parsed DateTimeOffset value.</param>
    /// <returns>True if parsing was successful, false otherwise</returns>
    public static bool TryParseUnixTimeMilliseconds(string str, out DateTimeOffset value)
    {
        if (long.TryParse(str, out long milliseconds) && TryParseUnixTimeMilliseconds(milliseconds, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
