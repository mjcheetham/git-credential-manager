using Microsoft.Identity.Client;

namespace GitCredentialManager.Authentication.Entra;

internal static class MsalExtensions
{
    extension<T>(BaseAbstractApplicationBuilder<T> builder) where T : BaseAbstractApplicationBuilder<T>
    {
        public T WithTraceLogging(ICommandContext context) =>
            WithTraceLogging(builder, context.Settings.IsMsalTracingEnabled,
                context.Settings.IsSecretTracingEnabled, context.Trace);

        public T WithTraceLogging(bool enable, bool includePii, ITrace trace)
        {
            if (enable)
            {
                return builder.WithLogging((level, message, _) =>
                        trace.WriteLine($"[{level.ToString()}] {message}", memberName: "MSAL"),
                    LogLevel.Verbose,
                    includePii,
                    enableDefaultPlatformLogging: false);
            }

            return (T)builder;
        }
    }
}
