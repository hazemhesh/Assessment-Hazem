namespace LoyaltyPoints.Api.Models;

public record Award(
    string AwardId,
    string SourceId,
    string ExternalTransactionId,
    string WalletId,
    int Points);
