using Gateway.Configuration;

namespace Gateway.Services;

public interface ITokenExchangeService
{
    Task<TokenExchangeResponse> Exchange(string accessToken, ApiConfigurationRecord apiConfig);
}