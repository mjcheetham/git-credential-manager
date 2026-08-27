using System;

namespace Microsoft.AzureRepos.Accounts;

public sealed class AccountReference : IEquatable<AccountReference>
{
    public AccountReference(string homeAccountId, string userName)
    {
        if (string.IsNullOrWhiteSpace(homeAccountId) && string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("An account ID or username is required.");
        }

        HomeAccountId = homeAccountId;
        UserName = userName;
    }

    public string HomeAccountId { get; }
    public string UserName { get; }
    public bool IsLegacy => string.IsNullOrWhiteSpace(HomeAccountId);

    public bool Equals(AccountReference other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(HomeAccountId, other.HomeAccountId) &&
               StringComparer.OrdinalIgnoreCase.Equals(UserName, other.UserName);
    }

    public override bool Equals(object obj) => obj is AccountReference other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            HomeAccountId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(HomeAccountId),
            UserName is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(UserName));
    }
}
