using System.ComponentModel.DataAnnotations;
using Application.Domino;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("start")]
public sealed class DominoController(DominoBatchService batchService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartGame(
        [FromQuery, Range(1, int.MaxValue)] int games = 1,
        CancellationToken cancellationToken = default)
    {
        await batchService.StartAsync(games, cancellationToken);
        return Accepted();
    }
}