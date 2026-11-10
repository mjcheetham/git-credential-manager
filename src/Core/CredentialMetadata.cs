using System;

namespace GitCredentialManager
{
    /// <summary>
    /// Optional, non-secret information associated with a credential.
    /// </summary>
    public sealed record CredentialMetadata
    {
        public static readonly CredentialMetadata Empty = new();

        /// <summary>
        /// True if the credential has an expiration date and that date is in the past, otherwise false.
        /// </summary>
        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt <= DateTimeOffset.UtcNow;

        /// <summary>
        /// The instant at which the credential expires, or null if unknown.
        /// </summary>
        public DateTimeOffset? ExpiresAt { get; init; }
    }
}
