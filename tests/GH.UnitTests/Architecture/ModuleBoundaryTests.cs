using System.Reflection;
using GymHub.Modules.Billing;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Discussions;
using GymHub.Modules.Discussions.Contracts;
using GymHub.Modules.Programs;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Trainers;
using GymHub.Modules.Trainers.Contracts;
using NetArchTest.Rules;

namespace GH.UnitTests.Architecture;

/// <summary>
/// Modules may use BuildingBlocks and other modules' .Contracts only; slices may use Shared and Domain but not each other.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(ProgramsModule).Assembly,
        typeof(SubscriptionsModule).Assembly,
        typeof(TrainersModule).Assembly,
        typeof(DiscussionsModule).Assembly,
        typeof(BillingModule).Assembly
    ];

    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(IProgramCatalog).Assembly,
        typeof(IEntitlements).Assembly,
        typeof(TrainerApproved).Assembly,
        typeof(DiscussionThreadStarted).Assembly,
        typeof(IPayoutReadiness).Assembly
    ];

    public static TheoryData<string> ModuleNames => [.. ModuleAssemblies.Select(a => a.GetName().Name!)];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Modules_do_not_reference_other_modules_implementations(string moduleName)
    {
        var module = ModuleAssemblies.Single(a => a.GetName().Name == moduleName);
        var otherModules = ModuleAssemblies.Where(a => a != module).Select(a => a.GetName().Name).ToHashSet();

        var offending = module.GetReferencedAssemblies().Select(r => r.Name).Where(otherModules.Contains);

        Assert.Empty(offending);
    }

    [Fact]
    public void Contracts_do_not_reference_module_implementations()
    {
        var modules = ModuleAssemblies.Select(a => a.GetName().Name).ToHashSet();

        var offending = ContractAssemblies
            .SelectMany(c => c.GetReferencedAssemblies().Select(r => (Contract: c.GetName().Name, Reference: r.Name)))
            .Where(x => modules.Contains(x.Reference));

        Assert.Empty(offending);
    }

    public static TheoryData<string> ModulesWithSlices => [.. ModuleAssemblies.Select(a => a.GetName().Name!)];

    [Theory]
    [MemberData(nameof(ModulesWithSlices))]
    public void Slices_do_not_depend_on_other_slices(string moduleName)
    {
        var module = ModuleAssemblies.Single(a => a.GetName().Name == moduleName);
        var featuresNamespace = $"{moduleName}.Features.";

        var slices = module.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null && ns.StartsWith(featuresNamespace, StringComparison.Ordinal))
            .Select(ns => ns!)
            .Where(ns => ns != $"{featuresNamespace}Shared")
            .Distinct()
            .ToList();

        Assert.NotEmpty(slices);

        foreach (var slice in slices)
        {
            var others = slices.Where(s => s != slice).ToArray();

            if (others.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(module)
                .That().ResideInNamespace(slice)
                .ShouldNot().HaveDependencyOnAny(others)
                .GetResult();

            Assert.True(result.IsSuccessful,
                $"{slice} depends on another slice: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
