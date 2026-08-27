using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.AzureRepos.Accounts;

public sealed class AccountBindingResult
{
    public AccountBindingResult(
        AccountBinding selectedBinding = null,
        AccountBinding suppressingBinding = null,
        IEnumerable<AccountBinding> invalidBindings = null)
    {
        if (selectedBinding?.State != AccountBindingState.Bound)
        {
            if (selectedBinding is not null)
            {
                throw new ArgumentException("Selected binding must be bound.", nameof(selectedBinding));
            }
        }

        if (suppressingBinding?.State != AccountBindingState.NoInherit)
        {
            if (suppressingBinding is not null)
            {
                throw new ArgumentException(
                    "Suppressing binding must be no-inherit.", nameof(suppressingBinding));
            }
        }

        if (selectedBinding is not null && suppressingBinding is not null)
        {
            throw new ArgumentException("A result cannot both select and suppress a binding.");
        }

        SelectedBinding = selectedBinding;
        SuppressingBinding = suppressingBinding;
        InvalidBindings = (invalidBindings ?? Array.Empty<AccountBinding>()).ToArray();

        if (InvalidBindings.Any(x => x?.State != AccountBindingState.Invalid))
        {
            throw new ArgumentException(
                "Invalid binding collection must contain only invalid records.", nameof(invalidBindings));
        }
    }

    public AccountBinding SelectedBinding { get; }
    public AccountBinding SuppressingBinding { get; }
    public IReadOnlyList<AccountBinding> InvalidBindings { get; }
    public bool HasBinding => SelectedBinding is not null;
    public bool IsInheritanceSuppressed => SuppressingBinding is not null;
    public bool NeedsMigration => SelectedBinding?.Account?.IsLegacy == true;
}
