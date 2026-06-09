namespace Microsoft.AzureRepos;

/// <summary>
/// Identifies a binding for the Azure Repos provider: what the binding applies to
/// (a tenant or an Azure DevOps organization) and whether it lives at the global or local
/// (per-clone) git-config level.
/// </summary>
/// <remarks>
/// Modelled as a sealed discriminated union so callers can pattern-match exhaustively at
/// compile time. The <c>IsLocal</c> flag carried by each case is what distinguishes a
/// per-clone override from a machine-wide preference; the same identity can exist at both
/// levels simultaneously, with local taking precedence on resolution.
/// </remarks>
public abstract record AzureReposBindingScope
{
    private AzureReposBindingScope() { }

    public static AzureReposBindingScope ForTenant(string tenantId, bool isLocal = false) =>
        new Tenant(tenantId, isLocal);
    public static AzureReposBindingScope ForOrg(string orgName, bool isLocal = false) =>
        new Org(orgName, isLocal);

    /// <summary>
    /// True if this scope lives at the per-clone (local) git-config level rather than the
    /// machine-wide (global) level.
    /// </summary>
    public abstract bool IsLocal { get; }

    /// <summary>
    /// "Use this account for any Azure DevOps organization backed by the given Microsoft Entra tenant."
    /// </summary>
    public sealed record Tenant(string TenantId, bool IsLocal = false) : AzureReposBindingScope
    {
        public override bool IsLocal { get; } = IsLocal;
    }

    /// <summary>
    /// "Use this account for this specific Azure DevOps organization, regardless of which tenant it lives in."
    /// </summary>
    public sealed record Org(string OrgName, bool IsLocal = false) : AzureReposBindingScope
    {
        public override bool IsLocal { get; } = IsLocal;
    }
}
