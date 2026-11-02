using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Songly.Common.Modules;

/// <summary>
/// Entry point of a domain module. The host registers and maps every module through this
/// interface and nothing else.
/// </summary>
/// <remarks>
/// Apart from the types in its <c>Contracts</c> namespace, the implementation of this interface
/// is the only public type a module may have. Everything else is <c>internal</c>, so another
/// module cannot reach it without the compiler refusing (see docs/adr/0002).
/// </remarks>
public interface IModule
{
    /// <summary>Registers the module's services in the shared container.</summary>
    void Register(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's HTTP endpoints.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
