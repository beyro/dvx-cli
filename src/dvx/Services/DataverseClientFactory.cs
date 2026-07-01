using dvx.Models;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace dvx.Services
{
    public static class DataverseClientFactory
    {
        private const string OAuthAppId       = "51f81489-12ee-4a9e-aaae-a2591f45987d";
        private const string OAuthRedirectUri = "http://localhost";

        public static ServiceClient Create(EnvironmentConfig env)
        {
            var connStr = env.AuthType == DataverseAuthType.Interactive
                ? InteractiveConnectionString(env.Url)
                : ClientSecretConnectionString(env);

            var client = new ServiceClient(connStr);
            if (!client.IsReady)
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse at '{env.Url}': {client.LastError}");

            return client;
        }

        private static string ClientSecretConnectionString(EnvironmentConfig env) =>
            $"AuthType=ClientSecret;" +
            $"Url={env.Url};" +
            $"ClientId={env.ClientId};" +
            $"ClientSecret={env.ClientSecret}";


        private static string InteractiveConnectionString(string url)
        {
            return $"AuthType=OAuth;" +
                   $"Url={url};" +
                   $"AppId={OAuthAppId};" +
                   $"RedirectUri={OAuthRedirectUri};" +
                   $"LoginPrompt=Auto;";
        }
    }
}
