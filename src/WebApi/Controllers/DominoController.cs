using Application.Domino;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("start")]
public sealed class DominoController(DominoGameService gameService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartGame(CancellationToken cancellationToken)
    {
        await gameService.StartAsync(cancellationToken);
        return Accepted();
    }
}