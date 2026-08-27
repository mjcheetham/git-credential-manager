using System;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication.Entra;

namespace Microsoft.AzureRepos.Accounts;

public interface IAccountBindingTargetResolver
{
    Task<AccountBindingTargetResolution> ResolveOrganizationAsync(string organization);
    Task<AccountBindingTargetResolution> ResolveTenantAsync(string tenant);
}

public sealed class AccountBindingTargetResolution
{
    public AccountBindingTargetResolution(
        AccountBindingTarget target,
        AccountBindingTarget tenant,
        string authority)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureArgument.NotNullOrWhiteSpace(authority, nameof(authority));

        if (tenant is not null && tenant.Kind != AccountBindingTargetKind.Tenant)
        {
            throw new ArgumentException("Resolved tenant target must be a tenant.", nameof(tenant));
        }

        Target = target;
        Tenant = tenant;
        Authority = authority;
    }

    public AccountBindingTarget Target { get; }
    public AccountBindingTarget Tenant { get; }
    public string Authority { get; }
}

public sealed class AccountBindingTargetResolver : IAccountBindingTargetResolver
{
    private readonly IAzureDevOpsRestApi _azureDevOps;
    private readonly IEntraTenantResolver _tenantResolver;

    public AccountBindingTargetResolver(ICommandContext context, IAzureDevOpsRestApi azureDevOps)
        : this(azureDevOps, CreateTenantResolver(context, azureDevOps)) { }

    public AccountBindingTargetResolver(
        IAzureDevOpsRestApi azureDevOps,
        IEntraTenantResolver tenantResolver)
    {
        EnsureArgument.NotNull(azureDevOps, nameof(azureDevOps));
        EnsureArgument.NotNull(tenantResolver, nameof(tenantResolver));

        _azureDevOps = azureDevOps;
        _tenantResolver = tenantResolver;
    }

    public async Task<AccountBindingTargetResolution> ResolveOrganizationAsync(string organization)
    {
        AccountBindingTarget target = AccountBindingTarget.ForOrganization(organization);
        Uri organizationUri = new UriBuilder(
            Uri.UriSchemeHttps,
            AzureDevOpsConstants.AzureDevOpsHost)
        {
            Path = organization
        }.Uri;

        string authority = await _azureDevOps.GetAuthorityAsync(organizationUri);
        AccountBindingTarget tenant = TryGetTenantId(authority, out Guid tenantId)
            ? AccountBindingTarget.ForTenant(tenantId)
            : null;

        return new AccountBindingTargetResolution(target, tenant, authority);
    }

    public async Task<AccountBindingTargetResolution> ResolveTenantAsync(string tenant)
    {
        EnsureArgument.NotNullOrWhiteSpace(tenant, nameof(tenant));

        EntraTenant resolved = await _tenantResolver.LookupAsync(tenant);
        if (resolved is null || resolved.Id == Guid.Empty)
        {
            return null;
        }

        AccountBindingTarget target = AccountBindingTarget.ForTenant(resolved.Id);
        return new AccountBindingTargetResolution(target, target, resolved.Authority);
    }

    private bool TryGetTenantId(string authority, out Guid tenantId)
    {
        tenantId = Guid.Empty;

        if (!Uri.TryCreate(authority, UriKind.Absolute, out Uri authorityUri))
        {
            return false;
        }

        Uri authorityBase = _azureDevOps.GetAuthorityBaseUri();
        if (!authorityBase.IsBaseOf(authorityUri))
        {
            return false;
        }

        string relative = Uri.UnescapeDataString(authorityBase.MakeRelativeUri(authorityUri).ToString());
        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        string tenant = segments[0];
        return Guid.TryParse(tenant, out tenantId) && tenantId != Guid.Empty;
    }

    private static IEntraTenantResolver CreateTenantResolver(
        ICommandContext context,
        IAzureDevOpsRestApi azureDevOps)
    {
        EnsureArgument.NotNull(context, nameof(context));
        EnsureArgument.NotNull(azureDevOps, nameof(azureDevOps));

        return new EntraTenantResolver(
            context.HttpClientFactory,
            azureDevOps.GetAuthorityBaseUri().ToString());
    }
}
