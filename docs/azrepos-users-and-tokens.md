# Azure Repos: Access tokens and Accounts

## Different credential types

The Azure Repos host provider supports creating multiple types of credential:

- Azure DevOps personal access tokens
- Microsoft identity OAuth tokens

To select which type of credential the Azure Repos host provider will create
and use, you can set the [`credential.azreposCredentialType`][credential-azreposCredentialType]
configuration entry (or [`GCM_AZREPOS_CREDENTIALTYPE`][gcm-azrepos-credential-type]
environment variable).

### Azure DevOps personal access tokens

Historically, the only option supported by the Azure Repos host provider was
Azure DevOps Personal Access Tokens (PATs).

These PATs are only used by Azure DevOps, and must be [managed through the Azure
DevOps user settings page][azure-devops-pats] or [REST API][azure-devops-api].

PATs have a limited lifetime and new tokens must be created once they expire. In
Git Credential Manager, when a PAT expired (or was manually revoked) this
resulted in a new authentication prompt.

### Microsoft identity OAuth tokens

"Microsoft identity OAuth token" is the generic term for OAuth-based access
tokens issued by Azure Active Directory for either Work and School Accounts
(AAD tokens) or Personal Accounts (Microsoft Account/MSA tokens).

Azure DevOps supports Git authentication using Microsoft identity OAuth tokens
as well as PATs. Microsoft identity OAuth tokens created by Git Credential
Manager are scoped to Azure DevOps only.

Unlike PATs, Microsoft identity OAuth tokens get automatically refreshed and
renewed as long as you are actively using them to perform Git operations.

These tokens are also securely shared with other Microsoft developer tools
including the Visual Studio IDE and Azure CLI. This means that as long as you're
using Git or one of these tools with the same account, you'll never need to
re-authenticate due to expired tokens!

#### User accounts

The first time you access an Azure DevOps organization with Microsoft identity
OAuth tokens, GCM prompts you to select an account. After authentication
succeeds, GCM records an account binding in Git configuration so it can select
the same cached account for later Git operations.

A binding stores both the account's stable Microsoft Entra home account ID and
its username. The account ID is used for selection, while the username remains
useful for display and compatibility with bindings written by older GCM
versions. A legacy username-only binding is upgraded automatically after a
successful authentication.

---

**Note:** If GCM is set to use PAT credentials, this account will **NOT** be
used and you will continue to be prompted to select a user account to renew the
credential. This may change in the future.

---

Bindings can target either an Azure DevOps organization or a Microsoft Entra
tenant. Organization bindings are more specific and always take precedence
over tenant bindings. Within each target, a local repository binding takes
precedence over a global user binding. The complete lookup order is:

1. Local organization binding.
2. Global organization binding.
3. Local tenant binding.
4. Global tenant binding.

Global bindings are stored in the user Git configuration, such as
`~/.gitconfig` or `%USERPROFILE%\.gitconfig`. Local bindings are stored in the
current repository's `.git/config`.

Normally GCM manages organization bindings automatically. For advanced
scenarios, use the `azure-repos` provider commands:

```shell
git-credential-manager azure-repos <command> <options>
```

##### Inspect accounts and bindings

`list` displays all cached Microsoft Entra accounts together with bindings at
the selected scope. This includes unbound accounts and stale, legacy, invalid,
or no-inherit bindings.

```shell
git-credential-manager azure-repos list
git-credential-manager azure-repos list --local
git-credential-manager azure-repos list --org contoso
git-credential-manager azure-repos list --tenant contoso.onmicrosoft.com
```

`show` displays the local and global records for one organization or tenant,
along with the effective resolution:

```shell
git-credential-manager azure-repos show --org contoso
git-credential-manager azure-repos show --tenant contoso.onmicrosoft.com
```

##### Manage the account cache

`login` authenticates and adds an account to the shared Microsoft Entra cache.
It does not create a binding.

```shell
git-credential-manager azure-repos login
git-credential-manager azure-repos login --org contoso
git-credential-manager azure-repos login --tenant contoso.onmicrosoft.com
```

`logout` removes one account from the shared cache. It does not remove its
bindings, which remain visible as stale records until the account signs in
again or the bindings are changed.

```shell
git-credential-manager azure-repos logout --id <home-account-id>
git-credential-manager azure-repos logout --username alice@contoso.com
```

The Microsoft Entra cache is shared with other Microsoft developer tools, so
logging out can affect those tools.

##### Manage bindings

Use `set` to bind an organization or tenant to exactly one cached account.
Specify the account by home account ID or by an unambiguous username. Global
scope is the default; use `--local` inside a repository to override it.

```shell
git-credential-manager azure-repos set --org contoso \
    --username alice@contoso.com
git-credential-manager azure-repos set --tenant contoso.onmicrosoft.com \
    --id <home-account-id>
git-credential-manager azure-repos set --local --org contoso \
    --username alice-alt@contoso.com
```

Use `unset` to remove the record at an exact scope and restore inheritance:

```shell
git-credential-manager azure-repos unset --org contoso
git-credential-manager azure-repos unset --local --org contoso
git-credential-manager azure-repos unset --tenant contoso.onmicrosoft.com
```

To prevent a repository from inheriting a binding, set a local no-inherit
record. An organization no-inherit record suppresses both global organization
and tenant fallback. A tenant no-inherit record suppresses only the global
binding for that tenant.

```shell
git-credential-manager azure-repos set --local --org contoso --no-inherit
git-credential-manager azure-repos set --local \
    --tenant contoso.onmicrosoft.com --no-inherit
```

When Git reports that an Entra credential was rejected, GCM automatically
writes a local organization no-inherit record if the operation is inside a
repository. The next credential request therefore prompts for an account
instead of repeatedly selecting the rejected inherited binding. After
authentication succeeds, GCM replaces that organization no-inherit record with
the newly selected account.

`clear-cache` clears cached Azure DevOps authority discovery. It does not clear
Microsoft Entra accounts or account bindings.

[azure-devops-pats]: https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops&tabs=preview-page
[credential-azreposCredentialType]: configuration.md#credentialazreposcredentialtype
[gcm-azrepos-credential-type]: environment.md#GCM_AZREPOS_CREDENTIALTYPE
[azure-devops-api]: https://docs.microsoft.com/en-gb/rest/api/azure/devops/tokens/pats
