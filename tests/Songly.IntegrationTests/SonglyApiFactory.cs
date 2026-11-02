using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Songly.IntegrationTests.SonglyApiFactory))]

namespace Songly.IntegrationTests;

/// <summary>
/// The whole application against a real Postgres in a container — never an in-memory substitute,
/// which would lie about how Postgres behaves. One instance is shared by the whole test assembly.
/// </summary>
public sealed class SonglyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same image as docker-compose and the original backend's tests.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Songly", ConnectionString);
        builder.ConfigureTestServices(services => services.AddSingleton<IModule, ProbeModule>());
    }
}
