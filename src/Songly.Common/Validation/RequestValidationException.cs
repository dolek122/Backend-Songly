using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Common.Validation;

internal sealed class RequestValidationException(string details)
    : ApiException(StatusCodes.Status400BadRequest, "VALIDATION_ERROR", details);
