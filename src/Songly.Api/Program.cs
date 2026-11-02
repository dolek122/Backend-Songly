using Npgsql;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using Songly.Api;
using Songly.Common.Modules;
using Songly.Common.Web;
using Songly.Migrations;

var builder = WebApplication.CreateBuilder(args);

// Render (like most PaaS) tells the process which port to bind through PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Structured JSON everywhere except local development, where a human reads the console.
builder.Services.AddSerilog((_, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.With<DropFrameworkRequestIdEnricher>();
    if (builder.Environment.IsDevelopment())
    {
        logger.WriteTo.Console();
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddSingleton(services => NpgsqlDataSource.Create(ConnectionString(services.GetRequiredService<IConfiguration>())));
builder.Services.AddSonglyWeb();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

foreach (var module in ModuleCatalog.All)
{
    builder.Services.AddSingleton(module);
    module.Register(builder.Services, builder.Configuration);
}

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    DatabaseMigrator.Migrate(ConnectionString(app.Configuration), app.Services.GetRequiredService<ILoggerFactory>());
}

app.UseSonglyWeb();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");

// Mapped from the container rather than from ModuleCatalog, so the tests can plug in an extra
// module with endpoints that exist only to exercise the shared infrastructure.
foreach (var module in app.Services.GetServices<IModule>())
{
    module.MapEndpoints(app);
}

app.Run();

static string ConnectionString(IConfiguration configuration) =>
    configuration.GetConnectionString("Songly")
    ?? throw new InvalidOperationException("ConnectionStrings:Songly is not configured");

/// <summary>The application's entry point; public so the integration tests can host it.</summary>
public partial class Program;
