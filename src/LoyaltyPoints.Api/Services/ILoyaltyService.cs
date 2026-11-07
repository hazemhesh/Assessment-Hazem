using LoyaltyPoints.Api.Dtos;
using LoyaltyPoints.Api.Models;

namespace LoyaltyPoints.Api.Services;

public interface ILoyaltyService
{
    Wallet? GetWallet(string walletId);
    AwardProcessResult ProcessAward(AwardRequest request);
}
