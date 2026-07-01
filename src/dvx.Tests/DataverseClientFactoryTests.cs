using dvx.Models;
using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class DataverseClientFactoryTests
    {
        [Fact]
        public void ClientSecretConnectionString_ReturnsCorrectFormat()
        {
            var env = new EnvironmentConfig
            {
                Url = "https://test.crm.dynamics.com",
                ClientId = "my-client-id",
                ClientSecret = "my-secret"
            };

            var connStr = DataverseClientFactory.ClientSecretConnectionString(env);

            connStr.ShouldContain("AuthType=ClientSecret");
            connStr.ShouldContain("Url=https://test.crm.dynamics.com");
            connStr.ShouldContain("ClientId=my-client-id");
            connStr.ShouldContain("ClientSecret=my-secret");
        }

        [Fact]
        public void InteractiveConnectionString_ReturnsCorrectFormat()
        {
            var url = "https://test.crm.dynamics.com";

            var connStr = DataverseClientFactory.InteractiveConnectionString(url);

            connStr.ShouldContain("AuthType=OAuth");
            connStr.ShouldContain($"Url={url}");
            connStr.ShouldContain("AppId=51f81489-12ee-4a9e-aaae-a2591f45987d");
            connStr.ShouldContain("RedirectUri=http://localhost");
            connStr.ShouldContain("LoginPrompt=Auto");
        }

        [Fact]
        public void GetConnectionString_UsesInteractive_WhenAuthTypeIsInteractive()
        {
            var env = new EnvironmentConfig
            {
                Url = "https://test.crm.dynamics.com",
                AuthType = DataverseAuthType.Interactive
            };

            var connStr = DataverseClientFactory.GetConnectionString(env);

            connStr.ShouldContain("AuthType=OAuth");
            connStr.ShouldContain("Url=https://test.crm.dynamics.com");
        }

        [Fact]
        public void GetConnectionString_UsesClientSecret_WhenAuthTypeIsClientSecret()
        {
            var env = new EnvironmentConfig
            {
                Url = "https://test.crm.dynamics.com",
                AuthType = DataverseAuthType.ClientSecret,
                ClientId = "cid",
                ClientSecret = "sec"
            };

            var connStr = DataverseClientFactory.GetConnectionString(env);

            connStr.ShouldContain("AuthType=ClientSecret");
            connStr.ShouldContain("ClientId=cid");
            connStr.ShouldContain("ClientSecret=sec");
        }
    }
}
