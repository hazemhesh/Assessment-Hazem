using LoyaltyPoints.Api.Models;

namespace LoyaltyPoints.Api.Dtos;

public record AwardResponse(
    string AwardId,
    string SourceId,
    string ExternalTransactionId,
    string WalletId,
    int Points)
{
    public static AwardResponse FromDomain(Award award) =>
        new(
            award.AwardId,
            award.SourceId,
            award.ExternalTransactionId,
            award.WalletId,
            award.Points);
}
