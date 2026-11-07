using LoyaltyPoints.Api.Dtos;
using LoyaltyPoints.Api.Services;
using LoyaltyPoints.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LoyaltyPoints.Api.Controllers;

[ApiController]
[Route("wallets")]
public class WalletsController : ControllerBase
{
    private readonly ILoyaltyService _loyaltyService;

    public WalletsController(ILoyaltyService loyaltyService)
    {
        _loyaltyService = loyaltyService;
    }

    [HttpGet("{walletId}")]
    public IActionResult GetWallet(string walletId)
    {
        if (!RequestValidator.TryValidateIdentifier(walletId, "walletId", out var errorMessage))
        {
            return BadRequest(new ErrorResponse(errorMessage!));
        }

        var wallet = _loyaltyService.GetWallet(walletId);
        if (wallet is null)
        {
            return NotFound(new ErrorResponse($"Wallet '{walletId}' was not found."));
        }

        return Ok(new WalletResponse(wallet.WalletId, wallet.Balance));
    }
}
