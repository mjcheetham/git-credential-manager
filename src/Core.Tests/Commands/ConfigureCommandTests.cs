using System;
using System.Threading.Tasks;
using GitCredentialManager.Commands;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests.Commands
{
    public class ConfigureCommandTests
    {
        [Fact]
        public async Task ConfigureCommand_ExecuteAsync_User_InvokesConfigurationServiceConfigureUser()
        {
            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.ConfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new ConfigureCommand(context, configService.Object);

            await command.ExecuteAsync(false, null);

            configService.Verify(x => x.ConfigureAsync(
                    It.Is<ConfigurationTarget>(t => t.Scope == ConfigurationScope.CurrentUser && t.Host.IsLocalhost)),
                Times.Once);
        }

        [Fact]
        public async Task ConfigureCommand_ExecuteAsync_System_InvokesConfigurationServiceConfigureSystem()
        {
            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.ConfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new ConfigureCommand(context, configService.Object);

            await command.ExecuteAsync(true, null);

            configService.Verify(x => x.ConfigureAsync(
                    It.Is<ConfigurationTarget>(t => t.Scope == ConfigurationScope.System && t.Host.IsLocalhost)),
                Times.Once);
        }

        [Fact]
        public async Task ConfigureCommand_ExecuteAsync_User_WSL_InvokesConfigurationServiceConfigureUser()
        {
            const string wslDistroName = "Ubuntu-22.04";

            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.ConfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new ConfigureCommand(context, configService.Object);

            await command.ExecuteAsync(false, wslDistroName);

            configService.Verify(x => x.ConfigureAsync(
                    It.Is<ConfigurationTarget>(t => t.Scope == ConfigurationScope.CurrentUser && !t.Host.IsLocalhost
                        && StringComparer.Ordinal.Equals(t.Host.WslDistribution, wslDistroName))),
                Times.Once);
        }

        [Fact]
        public async Task ConfigureCommand_ExecuteAsync_System_WSL_InvokesConfigurationServiceConfigureSystem()
        {
            const string wslDistroName = "Ubuntu-22.04";

            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.ConfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new ConfigureCommand(context, configService.Object);

            await command.ExecuteAsync(true, wslDistroName);

            configService.Verify(x => x.ConfigureAsync(
                    It.Is<ConfigurationTarget>(t => t.Scope == ConfigurationScope.System && !t.Host.IsLocalhost
                        && StringComparer.Ordinal.Equals(t.Host.WslDistribution, wslDistroName))),
                Times.Once);
        }
    }
}
