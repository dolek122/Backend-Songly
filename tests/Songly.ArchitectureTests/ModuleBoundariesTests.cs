using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Songly.Api;
using Songly.Common.Modules;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Songly.ArchitectureTests;

/// <summary>
/// Keeps the modular monolith modular. Breaking a boundary breaks no other test — the code just
/// slowly turns into an ordinary monolith, and nobody notices until extracting a module turns out
/// to be impossible.
/// </summary>
/// <remarks>
/// <para>
/// Two of the original backend's rules need no test here, because the compiler enforces them:
/// modules cannot form a cycle (project references cannot), and Common cannot know the domain
/// (every module references Common, so the reverse would be a cycle).
/// </para>
/// <para>
/// The criteria are structural, never a list of names: a new module gets the same discipline
/// without anyone touching this file. Before adding a rule, make it fail once with a deliberate
/// violation — a rule that always passes is worse than none.
/// </para>
/// </remarks>
public sealed partial class ModuleBoundariesTests
{
    private static readonly Assembly[] Modules = ModuleCatalog.All.Select(module => module.GetType().Assembly).ToArray();

    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(Modules).Build();

    [Fact]
    public void Every_module_in_the_build_is_registered_in_the_catalog()
    {
        var registered = ModuleCatalog.All.Select(module => module.GetType()).ToHashSet();

        var unregistered = Directory.GetFiles(AppContext.BaseDirectory, "Songly.*.dll")
            .Select(Assembly.LoadFrom)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(type))
            .Where(type => !registered.Contains(type))
            .Select(type => type.FullName);

        Assert.Empty(unregistered);
    }

    /// <summary>
    /// The rule that makes the compiler the first line of defence. If entities, repositories or
    /// services could be public, "reach only through Contracts" would be one keyword away from
    /// meaning nothing.
    /// </summary>
    [Fact]
    public void Modules_expose_only_their_contracts_and_entry_point()
    {
        var violations = Modules
            .SelectMany(module => module.GetExportedTypes())
            .Where(type => !typeof(IModule).IsAssignableFrom(type) && !ContractsNamespace().IsMatch(type.Namespace ?? string.Empty))
            .Select(type => type.FullName);

        Assert.Empty(violations);
    }

    /// <summary>
    /// <c>InternalsVisibleTo</c> is the one way around <c>internal</c>, so it may open a module only
    /// to that module's own tests.
    /// </summary>
    [Fact]
    public void Module_internals_are_visible_only_to_the_modules_own_tests()
    {
        var violations = Modules
            .SelectMany(module => module.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Where(attribute => attribute.AssemblyName != $"{module.GetName().Name}.Tests")
                .Select(attribute => $"{module.GetName().Name} -> {attribute.AssemblyName}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void Modules_reach_other_modules_only_through_their_contracts()
    {
        foreach (var module in Modules)
        {
            var others = Modules.Where(other => other != module).ToArray();

            Types().That().ResideInAssembly(module)
                .Should().NotDependOnAny(Types().That().ResideInAssembly(others[0], others[1..])
                    .And().DoNotResideInNamespaceMatching(ContractsNamespace().ToString()))
                .Because("a module talks to another one only through its Contracts (interfaces, events, DTOs)")
                .Check(Architecture);
        }
    }

    /// <summary>
    /// An endpoint that cannot see an entity cannot return one instead of a DTO — and with it every
    /// column, including the ones that must never leave the server.
    /// </summary>
    [Fact]
    public void Endpoints_do_not_see_entities()
    {
        Types().That().ResideInNamespaceMatching(@"^Songly\.\w+\.Http(\..+)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^Songly\.\w+\.Domain(\..+)?$"))
            .Because("endpoints work with request/response DTOs, never with persistence entities")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [GeneratedRegex(@"^Songly\.\w+\.Contracts(\..+)?$")]
    private static partial Regex ContractsNamespace();
}
