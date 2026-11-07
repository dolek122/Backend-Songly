using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Songly.Common.Push;

public static class PushExtensions
{
    private const string FcmScope = "https://www.googleapis.com/auth/firebase.messaging";

    /// <summary>
    /// The mail channel's arrangement (original ADR 0040): <c>log</c> by default, and <c>fcm</c> without the
    /// service account key <b>fails the start</b> — an instance that only looks like it sends pushes is a
    /// failure nobody notices until someone asks why their phone is silent.
    /// </summary>
    /// <remarks>
    /// Only Google's auth library, for the OAuth token of the service account (it caches the token and
    /// renews it before expiry); the send itself is one <c>POST</c> through <see cref="HttpClient"/>, as the
    /// original did instead of pulling in the whole Firebase Admin SDK.
    /// </remarks>
    public static IServiceCollection AddSonglyPush(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PushOptions>().Bind(configuration.GetSection(PushOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PushOptions>, PushOptionsValidator>();

        var provider = configuration[$"{PushOptions.Section}:Provider"] ?? PushProviders.Log;
        if (!string.Equals(provider, PushProviders.Fcm, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPushSender, LoggingPushSender>();
            return services;
        }

        services.AddHttpClient(FcmPushSender.HttpClientName, (sp, client) =>
        {
            client.BaseAddress = sp.GetRequiredService<IOptions<PushOptions>>().Value.FcmBaseUrl;
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<IPushSender>(sp =>
        {
            var credential = ServiceAccount(sp.GetRequiredService<IOptions<PushOptions>>().Value.FcmCredentialsJson!);
            var scoped = credential.ToGoogleCredential().CreateScoped(FcmScope);
            return new FcmPushSender(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(FcmPushSender.HttpClientName),
                credential.ProjectId,
                cancellationToken => ((ITokenAccess)scoped).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken));
        });
        return services;
    }

    internal static ServiceAccountCredential ServiceAccount(string json) =>
        CredentialFactory.FromJson<ServiceAccountCredential>(json);
}

/// <summary>Refuses to start with <c>fcm</c> and no usable service account key.</summary>
internal sealed class PushOptionsValidator : IValidateOptions<PushOptions>
{
    public ValidateOptionsResult Validate(string? name, PushOptions options)
    {
        if (!string.Equals(options.Provider, PushProviders.Fcm, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(options.Provider, PushProviders.Log, StringComparison.OrdinalIgnoreCase)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail($"{PushOptions.Section}:Provider must be '{PushProviders.Log}' or '{PushProviders.Fcm}'");
        }

        if (string.IsNullOrWhiteSpace(options.FcmCredentialsJson))
        {
            return ValidateOptionsResult.Fail($"{PushOptions.Section}:Provider=fcm requires {PushOptions.Section}:FcmCredentialsJson — without it the app would start without a working push channel");
        }

        try
        {
            _ = PushExtensions.ServiceAccount(options.FcmCredentialsJson);
            return ValidateOptionsResult.Success;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or Newtonsoft.Json.JsonException or FormatException)
        {
            return ValidateOptionsResult.Fail($"{PushOptions.Section}:FcmCredentialsJson is not a service account key");
        }
    }
}
