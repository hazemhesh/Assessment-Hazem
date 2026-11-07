namespace LoyaltyPoints.Api.Models;

// Composite key identifying an award by (sourceId, externalTransactionId).
public readonly record struct AwardIdentity(string SourceId, string ExternalTransactionId);
