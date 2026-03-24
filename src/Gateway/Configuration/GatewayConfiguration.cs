namespace Gateway.Configuration;

public static class GatewayConfiguration
{
    public static GatewayConfigurationRecord AddGatewayConfiguration(this IConfiguration config)
    {
        var gatewayConfig = new GatewayConfigurationRecord
        {
            Url = config.GetValue<string>("Gateway:Url", ""),
            SessionTimeoutInMin = config.GetValue<int>("Gateway:SessionTimeoutInMin", 60),
            TokenExchangeStrategy = config.GetValue<string>("Gateway:TokenExchangeStrategy", ""),

            Authority = config.GetValue<string>("OpenIdConnect:Authority", ""),
            ClientId = config.GetValue<string>("OpenIdConnect:ClientId", ""),
            ClientSecret = config.GetValue<string>("OpenIdConnect:ClientSecret", ""),
            Scopes = config.GetValue<string>("OpenIdConnect:Scopes", ""),
            LogoutUrl = config.GetValue<string>("OpenIdConnect:LogoutUrl", ""),
            QueryUserInfoEndpoint = config.GetValue<bool>("OpenIdConnect:QueryUserInfoEndpoint", true),

            ApiConfigs = config.GetSection("Apis").Get<ApiConfigurationRecord[]>()!
        };
        
        return gatewayConfig;
    }
}
