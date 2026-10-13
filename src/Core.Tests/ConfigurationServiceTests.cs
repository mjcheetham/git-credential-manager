using System.Threading.Tasks;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests
{
    public class ConfigurationServiceTests
    {
        [Fact]
        public async Task ConfigurationService_ConfigureAsync_System_ComponentsAreConfiguredWithSystem()
        {
            var context = new TestCommandContext();
            var service = new ConfigurationService(context);

            var component1 = new Mock<IConfigurableComponent>();
            var component2 = new Mock<IConfigurableComponent>();
            var component3 = new Mock<IConfigurableComponent>();

            service.AddComponent(component1.Object);
            service.AddComponent(component2.Object);
            service.AddComponent(component3.Object);

            var expectedTarget = new ConfigurationTarget(ConfigurationScope.System);

            await service.ConfigureAsync(expectedTarget);

            component1.Verify(x => x.ConfigureAsync(expectedTarget),
                Times.Once);
            component2.Verify(x => x.ConfigureAsync(expectedTarget),
                Times.Once);
            component3.Verify(x => x.ConfigureAsync(expectedTarget),
                Times.Once);
        }

        [Fact]
        public async Task ConfigurationService_ConfigureAsync_User_ComponentsAreConfiguredWithUser()
        {
            var context = new TestCommandContext();
            var service = new ConfigurationService(context);

            var component1 = new Mock<IConfigurableComponent>();
            var component2 = new Mock<IConfigurableComponent>();
            var component3 = new Mock<IConfigurableComponent>();

            service.AddComponent(component1.Object);
            service.AddComponent(component2.Object);
            service.AddComponent(component3.Object);

            var expectedTarget = new ConfigurationTarget(ConfigurationScope.CurrentUser);

            await service.ConfigureAsync(expectedTarget);

            component1.Verify(x => x.ConfigureAsync(expectedTarget),
                Times.Once);
            component2.Verify(x => x.ConfigureAsync(expectedTarget),
                Times.Once);
            component3.Verify(x => x.ConfigureAsync(expectedTarget),
                Times.Once);
        }

        [Fact]
        public async Task ConfigurationService_UnconfigureAsync_System_ComponentsAreUnconfiguredWithSystem()
        {
            var context = new TestCommandContext();
            var service = new ConfigurationService(context);

            var component1 = new Mock<IConfigurableComponent>();
            var component2 = new Mock<IConfigurableComponent>();
            var component3 = new Mock<IConfigurableComponent>();

            service.AddComponent(component1.Object);
            service.AddComponent(component2.Object);
            service.AddComponent(component3.Object);

            var expectedTarget = new ConfigurationTarget(ConfigurationScope.System);

            await service.UnconfigureAsync(expectedTarget);

            component1.Verify(x => x.UnconfigureAsync(expectedTarget),
                Times.Once);
            component2.Verify(x => x.UnconfigureAsync(expectedTarget),
                Times.Once);
            component3.Verify(x => x.UnconfigureAsync(expectedTarget),
                Times.Once);
        }

        [Fact]
        public async Task ConfigurationService_UnconfigureAsync_User_ComponentsAreUnconfiguredWithUser()
        {
            var context = new TestCommandContext();
            var service = new ConfigurationService(context);

            var component1 = new Mock<IConfigurableComponent>();
            var component2 = new Mock<IConfigurableComponent>();
            var component3 = new Mock<IConfigurableComponent>();

            service.AddComponent(component1.Object);
            service.AddComponent(component2.Object);
            service.AddComponent(component3.Object);

            var expectedTarget = new ConfigurationTarget(ConfigurationScope.CurrentUser);

            await service.UnconfigureAsync(expectedTarget);

            component1.Verify(x => x.UnconfigureAsync(expectedTarget),
                Times.Once);
            component2.Verify(x => x.UnconfigureAsync(expectedTarget),
                Times.Once);
            component3.Verify(x => x.UnconfigureAsync(expectedTarget),
                Times.Once);
        }
    }
}
