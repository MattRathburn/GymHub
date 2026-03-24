namespace Gateway.Configuration;

public record ApiConfigurationRecord
{
    public string ApiPath { get; set; } = "";
    public string ApiScopes { get; set; } = "";
    public string ApiAudience { get; set; } = "";
}
