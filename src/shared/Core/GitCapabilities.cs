using System;

namespace GitCredentialManager;

[Flags]
public enum GitCapabilities
{
    /// <summary>
    /// No advertised capabilities.
    /// </summary>
    None = 0,

    /// <summary>
    /// Supports the specific authentication type property `authtype`,
    /// as well as the `credential` and `ephemeral` properties.
    /// </summary>
    AuthType = 1 << 0,

    /// <summary>
    /// Supports the `state[] and `continue` properties.
    /// </summary>
    State = 1 << 1,
}
