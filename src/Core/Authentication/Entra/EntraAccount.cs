using System;
using Microsoft.Identity.Client;

namespace GitCredentialManager.Authentication.Entra;

public interface IEntraAccount : IEquatable<IEntraAccount>
{
    /// <summary>
    /// Opaque, stable identifier for the account in MSAL's cache. Use this to refer to
    /// the account from persistent records — it survives UPN renames.
    /// </summary>
    string HomeAccountId { get; }

    /// <summary>
    /// User principal name (typically an email address); suitable for display.
    /// </summary>
    string UserName { get; }
}

public sealed class EntraAccount : IEntraAccount
{
    internal static EntraAccount FromMsalAccount(IAccount msalAccount)
    {
        EnsureArgument.NotNull(msalAccount, nameof(msalAccount));
        return new EntraAccount(msalAccount.HomeAccountId.Identifier, msalAccount.Username)
        {
            MsalAccount = msalAccount
        };
    }

    /// <summary>
    /// Construct an account from a single identifier whose shape isn't known by the caller
    /// The identifier is structurally classified and placed in the matching slot:
    /// <list type="bullet">
    ///   <item><description>An MSAL <see cref="HomeAccountId"/> shape
    ///         (<c>&lt;object-id&gt;.&lt;tenant-id-guid&gt;</c>) is placed in
    ///         <see cref="HomeAccountId"/>.</description></item>
    ///   <item><description>Anything else is placed in <see cref="UserName"/>. This includes
    ///         well-formed UPNs (<c>local@domain</c>), ADFS-style HomeAccountIds (which
    ///         carry no tenant id and are indistinguishable from a bare username), and
    ///         unrecognised values - MSAL's UPN-fallback resolution then either
    ///         fuzzy-matches it or rejects it.</description></item>
    /// </list>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Throws when the identifier is null or whitespace.
    /// </exception>
    public static EntraAccount FromIdentifier(string identifier)
    {
        EnsureArgument.NotNullOrWhiteSpace(identifier, nameof(identifier));

        return IsHomeAccountIdShape(identifier)
            ? new EntraAccount(homeAccountId: identifier, userName: null)
            : new EntraAccount(homeAccountId: null, userName: identifier);
    }

    /// <summary>
    /// True when <paramref name="value"/> structurally matches an MSAL Microsoft Entra
    /// <c>HomeAccountId</c>: an <c>&lt;object-id&gt;.&lt;tenant-id&gt;</c> pair where the
    /// <c>tenant-id</c> suffix is a well-formed RFC 4122 GUID (a strong invariant for
    /// Entra ID) and the <c>object-id</c> prefix is non-empty.
    /// </summary>
    /// <remarks>
    /// Splits on the last <c>.</c> to mirror <c>Microsoft.Identity.Client.AccountId.ParseFromString</c>,
    /// which permits an object id to itself contain dots in edge scenarios (B2C, some
    /// guest accounts). Object ids are not required to be GUIDs; only the tenant id
    /// is checked. Does not validate that the resulting pair refers to a real account,
    /// and intentionally does not recognise the ADFS shape (single token, no dot) since
    /// it is indistinguishable from a bare username.
    /// </remarks>
    public static bool IsHomeAccountIdShape(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        int dot = value.LastIndexOf('.');
        if (dot <= 0 || dot >= value.Length - 1) return false;
        return Guid.TryParse(value.AsSpan(dot + 1), out _);
    }

    public EntraAccount(string homeAccountId, string userName)
    {
        UserName = userName;
        HomeAccountId = homeAccountId;
    }

    public string HomeAccountId { get; }
    public string UserName { get; }
    internal IAccount MsalAccount { get; init; }

    // Both fields are compared case-insensitively to match MSAL: AccountId.Equals on the
    // identifier uses OrdinalIgnoreCase, and UPNs are case-insensitive per RFC 5321.
    public bool Equals(IEntraAccount other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return StringComparer.OrdinalIgnoreCase.Equals(HomeAccountId, other.HomeAccountId)
               && StringComparer.OrdinalIgnoreCase.Equals(UserName, other.UserName);
    }

    public override bool Equals(object obj) => obj is IEntraAccount other && Equals(other);

    public override int GetHashCode()
    {
        int h1 = HomeAccountId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(HomeAccountId);
        int h2 = UserName      is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(UserName);
        unchecked { return (h1 * 397) ^ h2; }
    }
}
