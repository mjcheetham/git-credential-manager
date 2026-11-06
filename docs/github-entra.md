# GitHub sign-in with Microsoft Entra

Git Credential Manager (GCM) can use your Microsoft Entra ID work or school
account to sign in to a GitHub enterprise that uses
[Enterprise Managed Users][github-emu]. You do not need to create a personal
access token or enter a GitHub password when using this sign-in method.

<div align="center">
<img src="img/github-entra-signin.png" alt="GitHub sign-in with Microsoft Entra" width="500">
</div>

## Who can use this sign-in method?

Use Microsoft Entra sign-in when all of the following apply:

- Your GitHub account is an Enterprise Managed User on GitHub.com.
- Your enterprise uses Microsoft Entra ID with OpenID Connect (OIDC) single
  sign-on.
- Your organization has provisioned your managed GitHub account and granted it
  access to the repositories you need.

This is not a way to sign in to a personal GitHub account with a personal
Microsoft account, or to link those accounts together. It also does not apply
to enterprises that use SAML single sign-on, even if Microsoft Entra ID is
their identity provider. Use another available authentication method for
those accounts.

GCM currently offers this option by default for GitHub.com, not GitHub
Enterprise Server or GitHub Enterprise Cloud with data residency on GHE.com.
Seeing the Microsoft Entra option does not mean that your account or
enterprise supports it.

If you are unsure which setup your organization uses, ask your GitHub
enterprise administrator. Administrators can refer to GitHub's documentation
on [configuring OIDC for Enterprise Managed Users][github-oidc].

## Sign in

Use an HTTPS repository URL and run a Git operation, for example:

```shell
git clone https://github.com/OWNER/REPOSITORY.git
```

1. In the GCM GitHub sign-in dialog, select the **Microsoft Entra** tab and
   choose **Sign in with Microsoft**. If GCM presents a terminal menu instead,
   select **Microsoft Entra**.
2. If prompted, select the work or school account that your organization uses
   for your managed GitHub account.
3. Complete Microsoft sign-in, including any multi-factor authentication or
   device requirements requested by your organization.

Depending on your platform and configuration, Microsoft sign-in may use a
system sign-in dialog or a web browser. GCM may also reuse an existing
Microsoft sign-in session, such as from your operating system account manager.

After sign-in succeeds, GCM supplies Git with a GitHub credential and the Git
operation continues.

### Sign in before running Git

You can also start Microsoft Entra sign-in explicitly:

```shell
git-credential-manager github login --entra
```

This command targets GitHub.com by default. GCM determines your GitHub user
name from the resulting GitHub credential; it does not need to match your
Microsoft account's email address.

## How it works

GCM authenticates your work or school account with Microsoft Entra ID, then
exchanges the resulting Microsoft Entra access token for a GitHub access
token. Git uses the GitHub token, not your Microsoft password or the Microsoft
Entra token.

GCM stores the GitHub credential in your [configured credential
store][credential-stores] for subsequent Git operations. Microsoft sign-in
state is managed separately by the authentication broker or Microsoft
authentication cache. Removing a stored GitHub credential does not
necessarily sign you out of Microsoft on your device.

## Troubleshooting

### The Microsoft Entra option is missing

Check that your GCM version includes GitHub Microsoft Entra sign-in and that
your repository uses an HTTPS URL on GitHub.com.

An override of [credential.gitHubAuthModes][github-auth-modes] or
[GCM_GITHUB_AUTHMODES][github-auth-modes-env] can hide this option. The `oauth`
mode includes only `browser` and `device`; it does not include `entra`.
Changing the available modes does not enable OIDC for your enterprise.

If GCM already has a usable GitHub credential, Git may not show a sign-in
dialog at all. Use the explicit sign-in command above to start this method.

### Microsoft requests administrator approval or blocks sign-in

Your organization's Microsoft Entra policies may require administrator
consent, application assignment, multi-factor authentication, or a compliant
device. Follow the instructions in the Microsoft sign-in prompt, or contact
your organization's Microsoft Entra administrator with the error message and
error code. Changing GCM's authentication mode does not bypass those policies.

### Microsoft sign-in succeeds but GitHub rejects the request

Confirm that you used the correct work or school account and that your
enterprise uses Enterprise Managed Users with Entra OIDC, rather than SAML.
Your administrator must provision the corresponding managed GitHub account.
Successful Microsoft sign-in alone does not grant GitHub repository access.

If sign-in succeeds but a Git operation is denied, ask your GitHub
administrator to check your account's access to the repository.

[credential-stores]: credstores.md
[github-auth-modes]: configuration.md#credentialgithubauthmodes
[github-auth-modes-env]: environment.md#gcm_github_authmodes
[github-emu]: https://docs.github.com/en/enterprise-cloud@latest/admin/concepts/identity-and-access-management/enterprise-managed-users
[github-oidc]: https://docs.github.com/en/enterprise-cloud@latest/admin/managing-iam/configuring-authentication-for-enterprise-managed-users/configuring-oidc-for-enterprise-managed-users
