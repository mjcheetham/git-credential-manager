using System;
using GitCredentialManager;

namespace Microsoft.AzureRepos.Accounts;

public enum AccountBindingTargetKind
{
    Organization,
    Tenant
}

public sealed class AccountBindingTarget : IEquatable<AccountBindingTarget>
{
    private AccountBindingTarget(string organization)
    {
        Kind = AccountBindingTargetKind.Organization;
        Organization = organization;
    }

    private AccountBindingTarget(Guid tenantId)
    {
        Kind = AccountBindingTargetKind.Tenant;
        TenantId = tenantId;
    }

    public AccountBindingTargetKind Kind { get; }
    public string Organization { get; }
    public Guid TenantId { get; }

    public static AccountBindingTarget ForOrganization(string organization)
    {
        EnsureArgument.NotNullOrWhiteSpace(organization, nameof(organization));
        return new AccountBindingTarget(organization);
    }

    public static AccountBindingTarget ForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID must not be empty.", nameof(tenantId));
        }

        return new AccountBindingTarget(tenantId);
    }

    public bool Equals(AccountBindingTarget other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind == AccountBindingTargetKind.Organization
            ? StringComparer.OrdinalIgnoreCase.Equals(Organization, other.Organization)
            : TenantId == other.TenantId;
    }

    public override bool Equals(object obj) => obj is AccountBindingTarget other && Equals(other);

    public override int GetHashCode()
    {
        return Kind == AccountBindingTargetKind.Organization
            ? HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Organization))
            : HashCode.Combine(Kind, TenantId);
    }

    public override string ToString()
    {
        return Kind == AccountBindingTargetKind.Organization
            ? Organization
            : TenantId.ToString("D");
    }
}
