using System;
using System.Collections.Generic;
using GitCredentialManager.Authentication.Entra;

namespace Microsoft.AzureRepos.Accounts;

public interface IAccountBindingManager
{
    AccountBinding Get(AccountBindingTarget target, AccountBindingScope scope);

    AccountBindingResult Resolve(string organization, Guid? tenantId = null);

    IReadOnlyList<AccountBinding> GetAll(
        AccountBindingScope scope,
        AccountBindingTarget filter = null);

    void Set(
        AccountBindingTarget target,
        AccountBindingScope scope,
        IEntraAccount account);

    void SetNoInherit(AccountBindingTarget target);

    void Unset(AccountBindingTarget target, AccountBindingScope scope);
}
