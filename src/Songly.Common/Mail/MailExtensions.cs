using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Songly.Common.Mail;

public static class MailExtensions
{
    public static IServiceCollection AddSonglyMail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MailOptions>().Bind(configuration.GetSection(MailOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MailOptions>, MailOptionsValidator>();

        var provider = configuration[$"{MailOptions.Section}:Provider"] ?? MailProviders.Log;
        if (provider == MailProviders.Smtp)
        {
            services.AddSingleton<SmtpMailSender>();
            services.AddSingleton<QueuedMailSender>();
            services.AddSingleton<IMailSender>(provider => provider.GetRequiredService<QueuedMailSender>());
            services.AddHostedService(provider => provider.GetRequiredService<QueuedMailSender>());
        }
        else
        {
            // Synchronous: nothing to hide by timing in development, and a test can read the
            // message right after the response.
            services.AddSingleton<IMailSender, LoggingMailSender>();
        }

        return services;
    }
}
