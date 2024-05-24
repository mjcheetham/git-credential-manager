namespace GitCredentialManager.Tests.Objects;

public class NullBuildAgent : IBuildAgent
{
    public bool IsRunningOnBuildAgent => false;

    public string GetFederatedIdentityToken(string audience) => null;

    public void Register(IBuildAgentProvider provider) { }
}
