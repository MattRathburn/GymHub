namespace Gateway.Configuration;

public record GatewayConfigurationRecord
{
    public string Url { get; set; } = "";
    public int SessionTimeoutInMin { get; set; }
    public string TokenExchangeStrategy { get; set; } = "";
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scopes { get; set; } = "";
    public string LogoutUrl { get; set; } = "";
    public bool QueryUserInfoEndpoint { get; set; } = true;
    public string ApiScopes { get; set; } = "";
    public ApiConfigurationRecord[] ApiConfigs { get; set; } = { };
}
