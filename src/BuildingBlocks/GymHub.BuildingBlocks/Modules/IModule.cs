using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace GymHub.BuildingBlocks.Modules;

/// <summary>
/// Entry point of a module. The API host keeps an explicit list of modules and calls these at startup.
/// </summary>
public interface IModule
{
    /// <summary>Route segment under /api/v1, e.g. "programs".</summary>
    string Name { get; }

    void AddServices(IHostApplicationBuilder builder);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
