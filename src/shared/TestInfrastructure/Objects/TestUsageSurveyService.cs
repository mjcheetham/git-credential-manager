using System.Collections.Generic;
using GitCredentialManager.UsageSurvey;

namespace GitCredentialManager.Tests.Objects
{
    /// <summary>
    /// Default in-memory <see cref="GitCredentialManager.UsageSurvey.IUsageSurveyService"/> used by
    /// <see cref="TestCommandContext"/>. Records nothing by default; tests can flip
    /// <see cref="IsEnabled"/> to capture calls in <see cref="RecordedEvents"/>.
    /// </summary>
    public class TestUsageSurveyService : IUsageSurveyService
    {
        public bool IsEnabled { get; set; }

        public IList<(string ProviderId, bool FromCache, string AuthMethod)> RecordedEvents { get; }
            = new List<(string, bool, string)>();

        public void RecordGet(string providerId, bool fromCache, string authMethod)
        {
            if (IsEnabled)
            {
                RecordedEvents.Add((providerId, fromCache, authMethod));
            }
        }
    }
}
