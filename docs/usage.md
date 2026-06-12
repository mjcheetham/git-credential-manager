# Command-line usage

After installation, Git will use Git Credential Manager and you will only need
to interact with any authentication dialogs asking for credentials.
GCM stays invisible as much as possible, so ideally you’ll forget that you’re
depending on GCM at all.

Assuming GCM has been installed, use your favorite terminal to execute the
following commands to interact directly with GCM.

```shell
git credential-manager [<command> [<args>]]
```

## Commands

### --help / -h / -?

Displays a list of available commands.

### --version

Displays the current version.

### get / store / erase

Commands for interaction with Git. You shouldn't need to run these manually.

Read the [Git manual][git-credentials-custom-helpers] about custom helpers for
more information.

### configure/unconfigure

Set your user-level Git configuration (`~/.gitconfig`) to use GCM. If you pass
`--system` to these commands, they act on the system-level Git configuration
(`/etc/gitconfig`) instead.

### usage-survey

Manage Git Credential Manager's optional, privacy-preserving usage survey.
Usage survey is **off by default**. See the [usage survey documentation][gcm-usage-survey]
for the full list of fields collected and the privacy model.

The subcommands are:

- `usage-survey on` / `usage-survey off` — opt in / out (persists to your global
  git config).
- `usage-survey status` — show enabled/disabled state, the install id, queue
  depth, and dispatcher status.
- `usage-survey show` — run the dispatcher in the foreground and print every
  event being shipped to stdout. Useful for auditing what is being sent.
- `usage-survey id [--reset]` — print or regenerate the persistent install id.
- `usage-survey purge` — delete any queued events from disk.

### azure-repos

Interact with the Azure Repos host provider to bind/unbind user accounts to
Azure DevOps organizations or specific remote URLs, and manage the
authentication authority cache.

For more information about managing user account bindings see
[here][azure-access-tokens-ua].

[azure-access-tokens-ua]: azrepos-users-and-tokens.md#useraccounts
[git-credentials-custom-helpers]: https://git-scm.com/docs/gitcredentials#_custom_helpers
[gcm-usage-survey]: usage-survey.md

### github

Interact with the GitHub host provider to manage your accounts on GitHub.com and
GitHub Enterprise Server instances.
