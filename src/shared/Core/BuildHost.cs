using System.Collections.Generic;
using System.Linq;

namespace GitCredentialManager;

public interface IBuildAgentProvider
{
    /// <summary>
    /// Returns true if the process is currently running on a build host.
    /// </summary>
    bool IsRunningOnBuildAgent { get; }

    /// <summary>
    /// Get a token that is used to identify the build agent for the current build job.
    /// </summary>
    /// <param name="audience"></param>
    /// <returns>String representing a build host's identity token, or null.</returns>
    string GetFederatedIdentityToken(string audience);
}

public interface IBuildAgent : IBuildAgentProvider
{
    /// <summary>
    /// Register a build host provider.
    /// </summary>
    /// <param name="provider">Build host provider</param>
    void Register(IBuildAgentProvider provider);
}

public class BuildAgent : IBuildAgent
{
    private readonly IList<IBuildAgentProvider> _providers = new List<IBuildAgentProvider>();

    public void Register(IBuildAgentProvider provider)
    {
        _providers.Add(provider);
    }

    public bool IsRunningOnBuildAgent => _providers.Any(x => x.IsRunningOnBuildAgent);

    public string GetFederatedIdentityToken(string audience)
    {
        foreach (IBuildAgentProvider provider in _providers)
        {
            string token = provider.GetFederatedIdentityToken(audience);
            if (token is not null)
            {
                return token;
            }
        }

        return null;
    }
}
