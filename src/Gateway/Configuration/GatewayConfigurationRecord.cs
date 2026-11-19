namespace Gateway.Configuration;

public record GatewayConfigurationRecord
{
    public string Url { get; set; } = "";
    public int SessionTimeoutInMin { get; set; }
    public string TokenExchangeStrategy { get; set; } = "";
    public string Authority { get; set; } = "";

    /// <summary>
    /// Where the Gateway itself reads the realm's metadata and calls its token endpoint. Defaults to
    /// <see cref="Authority"/>; a deployment where browsers reach Keycloak through this Gateway sets Keycloak's
    /// private address, or the Gateway would wait on itself at startup.
    /// </summary>
    public string MetadataAuthority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scopes { get; set; } = "";
    public string LogoutUrl { get; set; } = "";
    public bool QueryUserInfoEndpoint { get; set; } = true;
    public string ApiScopes { get; set; } = "";
    public ApiConfigurationRecord[] ApiConfigs { get; set; } = { };
}
