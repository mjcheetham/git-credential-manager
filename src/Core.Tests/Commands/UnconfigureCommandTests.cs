using System;
using System.Threading.Tasks;
using GitCredentialManager.Commands;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests.Commands
{
    public class UnconfigureCommandTests
    {
        [Fact]
        public async Task UnconfigureCommand_ExecuteAsync_User_InvokesConfigurationServiceUnconfigureUser()
        {
            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.UnconfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new UnconfigureCommand(context, configService.Object);

            await command.ExecuteAsync(false, null);

            configService.Verify(x => x.UnconfigureAsync(
                It.Is<ConfigurationTarget>(
                    t => t.Scope == ConfigurationScope.CurrentUser && t.Host.IsLocalhost)),
                Times.Once);
        }

        [Fact]
        public async Task UnconfigureCommand_ExecuteAsync_System_InvokesConfigurationServiceUnconfigureSystem()
        {
            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.UnconfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new UnconfigureCommand(context, configService.Object);

            await command.ExecuteAsync(true, null);

            configService.Verify(x => x.UnconfigureAsync(
                It.Is<ConfigurationTarget>(
                    t => t.Scope == ConfigurationScope.System && t.Host.IsLocalhost)),
                Times.Once);
        }

        [Fact]
        public async Task UnconfigureCommand_ExecuteAsync_User_Wsl_InvokesConfigurationServiceUnconfigureUser()
        {
            const string wslDistroName = "Ubuntu-22.04";

            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.UnconfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new UnconfigureCommand(context, configService.Object);

            await command.ExecuteAsync(false, wslDistroName);

            configService.Verify(x => x.UnconfigureAsync(
                    It.Is<ConfigurationTarget>(
                        t => t.Scope == ConfigurationScope.CurrentUser && !t.Host.IsLocalhost &&
                             StringComparer.Ordinal.Equals(t.Host.WslDistribution, wslDistroName))),
                Times.Once);
        }

        [Fact]
        public async Task UnconfigureCommand_ExecuteAsync_System_Wsl_InvokesConfigurationServiceUnconfigureSystem()
        {
            const string wslDistroName = "Ubuntu-22.04";

            var configService = new Mock<IConfigurationService>();
            configService.Setup(x => x.UnconfigureAsync(It.IsAny<ConfigurationTarget>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var context = new TestCommandContext();
            var command = new UnconfigureCommand(context, configService.Object);

            await command.ExecuteAsync(true, wslDistroName);

            configService.Verify(x => x.UnconfigureAsync(
                    It.Is<ConfigurationTarget>(
                        t => t.Scope == ConfigurationScope.System && !t.Host.IsLocalhost &&
                             StringComparer.Ordinal.Equals(t.Host.WslDistribution, wslDistroName))),
                Times.Once);
        }
    }
}
