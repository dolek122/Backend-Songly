using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Common.Storage;

/// <summary>The storage failed — 502: the request was fine, a service behind us was not.</summary>
public sealed class ObjectStorageException(string message, Exception innerException)
    : ApiException(StatusCodes.Status502BadGateway, "STORAGE_ERROR", message, innerException);
