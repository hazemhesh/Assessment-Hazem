using LoyaltyPoints.Api.Dtos;
using LoyaltyPoints.Api.Models;

namespace LoyaltyPoints.Api.Services;

public class InMemoryLoyaltyService : ILoyaltyService
{
    private readonly object _syncLock = new();
    private readonly Dictionary<string, Wallet> _wallets;
    private readonly Dictionary<AwardIdentity, Award> _awards;

    public InMemoryLoyaltyService()
    {
        _wallets = new Dictionary<string, Wallet>(StringComparer.Ordinal)
        {
            ["W100"] = new Wallet("W100", 100),
            ["W200"] = new Wallet("W200", 50)
        };

        _awards = new Dictionary<AwardIdentity, Award>();
    }

    public Wallet? GetWallet(string walletId)
    {
        lock (_syncLock)
        {
            if (!_wallets.TryGetValue(walletId, out var wallet))
            {
                return null;
            }

            // Return a snapshot copy so callers outside the lock read a consistent balance
            return new Wallet(wallet.WalletId, wallet.Balance);
        }
    }

    public AwardProcessResult ProcessAward(AwardRequest request)
    {
        var identity = new AwardIdentity(request.SourceId, request.ExternalTransactionId);

        lock (_syncLock)
        {
            // Check existing award by (sourceId, externalTransactionId) first (409 / 200 precedence before 404)
            if (_awards.TryGetValue(identity, out var existingAward))
            {
                var isMatchingRetry =
                    string.Equals(existingAward.WalletId, request.WalletId, StringComparison.Ordinal) &&
                    existingAward.Points == request.Points;

                if (isMatchingRetry)
                {
                    return new AwardProcessResult(AwardOutcome.MatchingRetry, existingAward);
                }

                return new AwardProcessResult(
                    AwardOutcome.ConflictingRetry,
                    ErrorMessage: $"Award with sourceId '{request.SourceId}' and externalTransactionId '{request.ExternalTransactionId}' already exists with different parameters.");
            }

            if (!_wallets.TryGetValue(request.WalletId, out var wallet))
            {
                return new AwardProcessResult(
                    AwardOutcome.WalletNotFound,
                    ErrorMessage: $"Wallet '{request.WalletId}' was not found.");
            }

            var newAward = new Award(
                AwardId: Guid.NewGuid().ToString("N"),
                SourceId: request.SourceId,
                ExternalTransactionId: request.ExternalTransactionId,
                WalletId: request.WalletId,
                Points: request.Points);

            wallet.Balance += request.Points;
            _awards.Add(identity, newAward);

            return new AwardProcessResult(AwardOutcome.Created, newAward);
        }
    }
}
