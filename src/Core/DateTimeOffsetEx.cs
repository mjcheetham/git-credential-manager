using System;
using System.Globalization;

namespace GitCredentialManager;

public static class DateTimeOffsetEx
{
    /// <summary>
    /// Try to parse a string containing a Unix timestamp in seconds.
    /// </summary>
    /// <param name="str">String containing a Unix timestamp in seconds.</param>
    /// <param name="value">Parsed timestamp.</param>
    /// <returns>
    /// True if the string is an integer in the range supported by <see cref="DateTimeOffset"/>, otherwise false.
    /// </returns>
    public static bool TryParseUnixTimeSeconds(string str, out DateTimeOffset value)
    {
        if (long.TryParse(str, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long seconds) &&
            seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() &&
            seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }

        value = default;
        return false;
    }
}
