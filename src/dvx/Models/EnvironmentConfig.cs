namespace dvx.Models
{
    public enum DataverseAuthType { ClientSecret, Interactive }

    public class EnvironmentConfig
    {
        public string  Name         { get; set; } = string.Empty;
        public string  Url          { get; set; } = string.Empty;
        public string  ClientId     { get; set; } = string.Empty;
        public string  ClientSecret { get; set; } = string.Empty;
        public DataverseAuthType AuthType { get; set; } = DataverseAuthType.ClientSecret;
    }
}
