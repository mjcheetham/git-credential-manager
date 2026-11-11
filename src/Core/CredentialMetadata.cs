using System;

namespace GitCredentialManager;

/// <summary>
/// Optional, non-secret information stored alongside a credential.
/// </summary>
/// <remarks>
/// Metadata does not identify a credential and does not affect credential lookup.
/// </remarks>
public sealed record CredentialMetadata
{
    /// <summary>
    /// Metadata with no values set.
    /// </summary>
    public static CredentialMetadata Empty { get; } = new();

    /// <summary>
    /// The instant at which the credential expires, or null if unknown.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// True if the credential has an expiry and that instant has passed, otherwise false.
    /// </summary>
    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow;
}
