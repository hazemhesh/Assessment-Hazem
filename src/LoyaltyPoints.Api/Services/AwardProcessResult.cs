using LoyaltyPoints.Api.Models;

namespace LoyaltyPoints.Api.Services;

public enum AwardOutcome
{
    Created,
    MatchingRetry,
    ConflictingRetry,
    WalletNotFound
}

public record AwardProcessResult(
    AwardOutcome Outcome,
    Award? Award = null,
    string? ErrorMessage = null);
