using dvx.Models;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace dvx.Services
{
    public static class DataverseClientFactory
    {
        public static ServiceClient Create(EnvironmentConfig env)
        {
            var connStr = $"AuthType=ClientSecret;" +
                          $"Url={env.Url};" +
                          $"ClientId={env.ClientId};" +
                          $"ClientSecret={env.ClientSecret}";

            var client = new ServiceClient(connStr);
            if (!client.IsReady)
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse at '{env.Url}': {client.LastError}");

            return client;
        }
    }
}
