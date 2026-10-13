using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitCredentialManager
{
    /// <summary>
    /// Targets for performing configuration changes using the <see cref="IConfigurationService"/>.
    /// </summary>
    public class ConfigurationTarget : IEquatable<ConfigurationTarget>
    {
        public ConfigurationTarget(ConfigurationScope scope)
            : this(ConfigurationHost.Localhost, scope) { }

        public ConfigurationTarget(ConfigurationHost host, ConfigurationScope scope)
        {
            EnsureArgument.NotNull(host, nameof(host));
            Host = host;
            Scope = scope;
        }

        /// <summary>
        /// Target host to apply configuration changes.
        /// </summary>
        public ConfigurationHost Host { get; }

        /// <summary>
        /// Target scope to apply configuration changes.
        /// </summary>
        public ConfigurationScope Scope { get; }

        public IGit GetGit(ICommandContext context)
        {
            if (Host.IsLocalhost)
            {
                return context.Git;
            }

            if (WslUtils.DistributionExists(Host.WslDistribution) &&
                WslUtils.TryLocateExecutable(Host.WslDistribution, "git", out string wslGit))
            {
                return new GitProcess(context.Trace, context.ProcessManager, wslGit);
            }

            throw new InvalidOperationException($"Unable to locate Git in WSL distribution '{Host.WslDistribution}'.");
        }

        #region IEquatable

        public bool Equals(ConfigurationTarget other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Host.Equals(other.Host) && Scope == other.Scope;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ConfigurationTarget)obj);
        }

        public override int GetHashCode() => HashCode.Combine(Host, (int)Scope);

        #endregion
    }

    public class ConfigurationHost
    {
        /// <summary>
        /// The local host/computer.
        /// </summary>
        public static ConfigurationHost Localhost { get; } = new() { IsLocalhost = true };

        /// <summary>
        /// A Windows Subsystem for Linux (WSL) distribution.
        /// </summary>
        /// <param name="distroName">Name of the WSL distribution.</param>
        public static ConfigurationHost Wsl(string distroName)
        {
            EnsureArgument.NotNullOrWhiteSpace(distroName, nameof(distroName));
            return new ConfigurationHost { WslDistribution = distroName };
        }

        public bool IsLocalhost { get; private init; }

        public string WslDistribution { get; private init; }

        private ConfigurationHost() { }
    }

    public enum ConfigurationScope
    {
        /// <summary>
        /// Scoped to the current user only.
        /// </summary>
        CurrentUser,

        /// <summary>
        /// Scoped to the entire system (all users).
        /// </summary>
        System
    }

    /// <summary>
    /// Component that requires Git or environment configuration to work.
    /// </summary>
    public interface IConfigurableComponent
    {
        /// <summary>
        /// Name of the component that requires configuration.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Configure the environment and Git to work with this hosting provider.
        /// </summary>
        /// <param name="target">Configuration target.</param>
        Task ConfigureAsync(ConfigurationTarget target);

        /// <summary>
        /// Remove changes to the environment and Git configuration previously made with <see cref="ConfigureAsync"/>.
        /// </summary>
        /// <param name="target">Configuration target.</param>
        Task UnconfigureAsync(ConfigurationTarget target);
    }

    public interface IConfigurationService
    {
        /// <summary>
        /// Add a <see cref="IConfigurableComponent"/> to the collection of components that will be configured.
        /// </summary>
        /// <param name="component"></param>
        void AddComponent(IConfigurableComponent component);

        /// <summary>
        /// Configure all components.
        /// </summary>
        /// <param name="target">Target level to configure.</param>
        Task ConfigureAsync(ConfigurationTarget target);

        /// <summary>
        /// Unconfigure all components.
        /// </summary>
        /// <param name="target">Target level to unconfigure.</param>
        Task UnconfigureAsync(ConfigurationTarget target);
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly ICommandContext _context;
        private readonly IList<IConfigurableComponent> _components = new List<IConfigurableComponent>();

        public ConfigurationService(ICommandContext context)
        {
            EnsureArgument.NotNull(context, nameof(context));

            _context = context;
        }

        #region IConfigurationService

        public void AddComponent(IConfigurableComponent component)
        {
            _components.Add(component);
        }

        public async Task ConfigureAsync(ConfigurationTarget target)
        {
            foreach (IConfigurableComponent component in _components)
            {
                _context.Trace.WriteLine($"Configuring component '{component.Name}'...");
                _context.Console.WriteLine($"Configuring component '{component.Name}'...");
                await component.ConfigureAsync(target);
            }
        }

        public async Task UnconfigureAsync(ConfigurationTarget target)
        {
            foreach (IConfigurableComponent component in _components)
            {
                _context.Trace.WriteLine($"Unconfiguring component '{component.Name}'...");
                _context.Console.WriteLine($"Unconfiguring component '{component.Name}'...");
                await component.UnconfigureAsync(target);
            }
        }

        #endregion
    }
}
