using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GitCredentialManager.Commands
{
    /// <summary>
    /// Acquire a new <see cref="GitCredential"/> from a <see cref="IHostProvider"/>.
    /// </summary>
    public class GetCommand : GitCommandBase
    {
        public GetCommand(ICommandContext context, IHostProviderRegistry hostProviderRegistry)
            : base(context, "get", "[Git] Return a stored credential", hostProviderRegistry) { }

        protected override async Task ExecuteInternalAsync(InputArguments input, IHostProvider provider)
        {
            GetCredentialResponse response = await provider.GetCredentialAsync(input);
            ICredential credential = response.Credential;

            var output = new Dictionary<string, IList<string>>();

            // Echo protocol, host, and path back at Git
            if (input.Protocol != null)
            {
                output["protocol"] = [ input.Protocol ];
            }
            if (input.Host != null)
            {
                output["host"] = [ input.Host ];
            }
            if (input.Path != null)
            {
                output["path"] = [ input.Path ];
            }

            // Add any state if supported
            if (response.State.Any())
            {
                if ((input.Capabilities & GitCapabilities.State) == 0)
                {
                    throw new Exception("State not supported with this version of Git!");
                }

                output["state"] = [];
                foreach (var kvp in response.State)
                {
                    output["state"].Add($"gcm.{kvp.Key}={kvp.Value}");
                }
            }

            // Set the continue flag if requested
            if (response.Continue)
            {
                output["continue"] = [ "1" ];
            }

            // Return the credential to Git
            output["username"] = [ credential.Account ];
            output["password"] = [ credential.Password ];

            Context.Trace.WriteLine("Writing credentials to output:");
            Context.Trace.WriteDictionarySecrets(output, new []{ "password" }, StringComparer.OrdinalIgnoreCase);

            // Write the values to standard out
            Context.Streams.Out.WriteDictionary(output);
        }
    }
}
