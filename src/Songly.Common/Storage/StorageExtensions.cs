using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Songly.Common.Storage;

public static class StorageExtensions
{
    public static IServiceCollection AddSonglyStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.Section))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Bucket) && Uri.IsWellFormedUriString(o.PublicBaseUrl, UriKind.Absolute) && !o.PublicBaseUrl.EndsWith('/'),
                $"{StorageOptions.Section}: Bucket is required and PublicBaseUrl must be an absolute URL without a trailing slash")
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(provider => CreateClient(provider.GetRequiredService<IOptions<StorageOptions>>().Value));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        return services;
    }

    private static AmazonS3Client CreateClient(StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.PathStyleAccess,
            Timeout = options.Timeout,

            // Since 2025 the SDK adds CRC checksums to every request by default; R2 and older MinIO
            // reject or mishandle them. Only where the operation requires one.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        // An empty endpoint means real AWS: the SDK builds the address from the region.
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            config.ServiceURL = options.Endpoint;
            config.AuthenticationRegion = options.Region;
        }

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }
}
