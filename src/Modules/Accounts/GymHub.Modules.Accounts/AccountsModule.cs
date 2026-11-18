using FluentValidation;
using GymHub.BuildingBlocks.Identity;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Accounts.Data;
using GymHub.Modules.Accounts.Features.AccountStatus;
using GymHub.Modules.Accounts.Features.ExportData;
using GymHub.Modules.Accounts.Features.ScheduleDeletion;
using GymHub.Modules.Accounts.Features.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Accounts;

/// <summary>
/// The member's own account: download their data, and delete the account after a grace period. The data itself
/// lives in the other modules, which each register an IPersonalDataSource.
/// </summary>
public sealed class AccountsModule : IModule
{
    public string Name => "account";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<AccountsDbContext>(AccountsDbContext.SchemaName);
        builder.AddIdentityAdmin();

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<AccountsModule>(includeInternalTypes: true);
        services.AddScoped<PersonalData>();
        services.AddMemoryCache();
        services.AddScoped<DeletedAccounts>();
        services.AddScoped<IDeletedAccounts>(sp => sp.GetRequiredService<DeletedAccounts>());

        services.AddScoped<AccountStatusHandler>();
        services.AddScoped<ExportDataHandler>();
        services.AddScoped<ScheduleDeletionHandler>();

        services.AddSingleton<AccountDeletionJob>();
        services.AddHostedService(sp => sp.GetRequiredService<AccountDeletionJob>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AccountStatusEndpoint.Map(endpoints);
        ExportDataEndpoint.Map(endpoints);
        ScheduleDeletionEndpoint.Map(endpoints);
    }
}
