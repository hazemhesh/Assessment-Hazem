using System.Text.Json;
using LoyaltyPoints.Api.Dtos;
using LoyaltyPoints.Api.Services;
using LoyaltyPoints.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LoyaltyPoints.Api.Controllers;

[ApiController]
[Route("awards")]
public class AwardsController : ControllerBase
{
    private readonly ILoyaltyService _loyaltyService;

    public AwardsController(ILoyaltyService loyaltyService)
    {
        _loyaltyService = loyaltyService;
    }

    [HttpPost]
    public IActionResult CreateAward([FromBody] JsonElement body)
    {
        if (!RequestValidator.TryValidateAwardElement(body, out var awardRequest, out var validationError))
        {
            return BadRequest(new ErrorResponse(validationError!));
        }

        var result = _loyaltyService.ProcessAward(awardRequest!);

        return result.Outcome switch
        {
            AwardOutcome.Created => StatusCode(StatusCodes.Status201Created, AwardResponse.FromDomain(result.Award!)),
            AwardOutcome.MatchingRetry => Ok(AwardResponse.FromDomain(result.Award!)),
            AwardOutcome.ConflictingRetry => Conflict(new ErrorResponse(result.ErrorMessage!)),
            AwardOutcome.WalletNotFound => NotFound(new ErrorResponse(result.ErrorMessage!)),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse("Unexpected award outcome."))
        };
    }
}
