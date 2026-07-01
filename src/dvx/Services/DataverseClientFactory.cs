using dvx.Models;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace dvx.Services
{
    public static class DataverseClientFactory
    {
        // Well-known Microsoft public client + redirect for Dataverse interactive sign-in, per
        // "Use OAuth authentication with Microsoft Dataverse". No app registration needed locally.
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

        /// <summary>
        /// Browser sign-in via the built-in public client. The SDK persists the MSAL token cache
        /// at <see cref="TokenCachePath"/> (OS-encrypted: DPAPI on Windows, Keychain on macOS,
        /// libsecret on Linux), so later commands reuse the token without another browser prompt.
        /// </summary>
        private static string InteractiveConnectionString(string url)
        {
            var cachePath = TokenCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            return $"AuthType=OAuth;" +
                   $"Url={url};" +
                   $"AppId={OAuthAppId};" +
                   $"RedirectUri={OAuthRedirectUri};" +
                   $"LoginPrompt=Auto;" +
                   $"TokenCacheStorePath={cachePath}";
        }

        internal static string TokenCachePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dvx", "msal_cache.data");
    }
}
