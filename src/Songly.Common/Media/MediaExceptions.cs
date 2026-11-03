using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Common.Media;

public sealed class UnsupportedImageException()
    : ApiException(StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_IMAGE", "Obsługiwane formaty obrazu to JPEG, PNG i WebP");

public sealed class EmptyUploadException()
    : ApiException(StatusCodes.Status400BadRequest, "EMPTY_UPLOAD", "Przesłany plik jest pusty");
