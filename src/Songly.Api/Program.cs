using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using Songly.Api;
using Songly.Common.Events;
using Songly.Common.Mail;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Common.Storage;
using Songly.Common.Throttling;
using Songly.Common.Validation;
using Songly.Common.Web;
using Songly.Migrations;

var builder = WebApplication.CreateBuilder(args);

// Render (like most PaaS) tells the process which port to bind through PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Structured JSON everywhere except on a developer's machine, where a human reads the console.
// "Local" is Development plus the test accounts, for clicking through the mobile app.
var humanReadableLogs = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Local");
builder.Services.AddSerilog((_, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.With<DropFrameworkRequestIdEnricher>();
    if (humanReadableLogs)
    {
        logger.WriteTo.Console();
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddSonglyDatabase();
builder.Services.AddSonglyEvents();
builder.Services.AddSonglyRedis(builder.Configuration);
builder.Services.AddSonglyMail(builder.Configuration);
builder.Services.AddSonglyStorage(builder.Configuration);
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
    DatabaseMigrator.Migrate(DatabaseExtensions.ConnectionString(app.Configuration), app.Services.GetRequiredService<ILoggerFactory>());
}

// Taken from the container rather than from ModuleCatalog, so the tests can plug in an extra
// module with endpoints that exist only to exercise the shared infrastructure.
var modules = app.Services.GetServices<IModule>().ToList();

app.UseSonglyWeb();
foreach (var module in modules)
{
    module.UsePipeline(app);
}

// Operational endpoints are public: a load balancer probing /health has no token.
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

var api = app.MapGroup(string.Empty).ValidateRequests();
foreach (var module in modules)
{
    module.MapEndpoints(api);
}

app.Run();

/// <summary>The application's entry point; public so the integration tests can host it.</summary>
public partial class Program;
