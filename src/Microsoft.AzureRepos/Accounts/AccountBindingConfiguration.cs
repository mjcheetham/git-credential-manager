using System;
using System.Globalization;
using GitCredentialManager;

namespace Microsoft.AzureRepos.Accounts;

internal enum AccountBindingProperty
{
    AccountId,
    UserName
}

internal static class AccountBindingConfiguration
{
    public static string GetKey(AccountBindingTarget target, AccountBindingProperty property)
    {
        EnsureArgument.NotNull(target, nameof(target));

        string targetType;
        string targetValue;

        switch (target.Kind)
        {
            case AccountBindingTargetKind.Organization:
                targetType = AzureDevOpsConstants.UrnOrgPrefix;
                targetValue = target.Organization.ToLowerInvariant();
                break;
            case AccountBindingTargetKind.Tenant:
                targetType = AzureDevOpsConstants.UrnTenantPrefix;
                targetValue = target.TenantId.ToString("D");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target.Kind, "Unsupported binding target.");
        }

        string propertyName = property switch
        {
            AccountBindingProperty.AccountId => AzureDevOpsConstants.GitConfiguration.Credential.AccountId,
            AccountBindingProperty.UserName => Constants.GitConfiguration.Credential.UserName,
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported binding property.")
        };

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}:{2}/{3}.{4}",
            Constants.GitConfiguration.Credential.SectionName,
            AzureDevOpsConstants.UrnScheme,
            targetType,
            targetValue,
            propertyName);
    }

    public static bool TryParseKey(
        string key,
        out AccountBindingTarget target,
        out AccountBindingProperty property)
    {
        target = null;
        property = default;

        if (!GitConfigurationKeyComparer.TrySplit(
                key, out string section, out string scope, out string propertyName) ||
            !GitConfigurationKeyComparer.SectionComparer.Equals(
                section, Constants.GitConfiguration.Credential.SectionName) ||
            !TryParseProperty(propertyName, out property) ||
            !Uri.TryCreate(scope, UriKind.Absolute, out Uri uri) ||
            !StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, AzureDevOpsConstants.UrnScheme))
        {
            return false;
        }

        string path = uri.AbsolutePath;
        string organizationPrefix = $"{AzureDevOpsConstants.UrnOrgPrefix}/";
        string tenantPrefix = $"{AzureDevOpsConstants.UrnTenantPrefix}/";

        if (path.StartsWith(organizationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string organization = path.Substring(organizationPrefix.Length);
            if (string.IsNullOrWhiteSpace(organization) || organization.Contains('/'))
            {
                return false;
            }

            target = AccountBindingTarget.ForOrganization(organization);
            return true;
        }

        if (path.StartsWith(tenantPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string tenant = path.Substring(tenantPrefix.Length);
            if (!Guid.TryParse(tenant, out Guid tenantId) || tenantId == Guid.Empty)
            {
                return false;
            }

            target = AccountBindingTarget.ForTenant(tenantId);
            return true;
        }

        return false;
    }

    private static bool TryParseProperty(string value, out AccountBindingProperty property)
    {
        if (GitConfigurationKeyComparer.PropertyComparer.Equals(
                value, AzureDevOpsConstants.GitConfiguration.Credential.AccountId))
        {
            property = AccountBindingProperty.AccountId;
            return true;
        }

        if (GitConfigurationKeyComparer.PropertyComparer.Equals(
                value, Constants.GitConfiguration.Credential.UserName))
        {
            property = AccountBindingProperty.UserName;
            return true;
        }

        property = default;
        return false;
    }
}
