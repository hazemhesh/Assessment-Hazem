using LoyaltyPoints.Api.Models;

namespace LoyaltyPoints.Api.Services;

public interface ILoyaltyService
{
    Wallet? GetWallet(string walletId);
}
