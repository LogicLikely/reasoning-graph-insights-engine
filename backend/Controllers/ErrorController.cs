using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/error")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ErrorController : ControllerBase
{
    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpPatch]
    [HttpDelete]
    public IActionResult Handle()
    {
        var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerFeature>();

        return Problem(
            detail: exceptionFeature?.Error.Message,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.");
    }
}
