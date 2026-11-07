namespace LoyaltyPoints.Api.Dtos;

public record AwardRequest(
    string SourceId,
    string ExternalTransactionId,
    string WalletId,
    int Points);
