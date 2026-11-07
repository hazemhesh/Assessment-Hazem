using LoyaltyPoints.Api.Models;

namespace LoyaltyPoints.Api.Services;

public class InMemoryLoyaltyService : ILoyaltyService
{
    private readonly object _syncLock = new();
    private readonly Dictionary<string, Wallet> _wallets;

    public InMemoryLoyaltyService()
    {
        _wallets = new Dictionary<string, Wallet>(StringComparer.Ordinal)
        {
            ["W100"] = new Wallet("W100", 100),
            ["W200"] = new Wallet("W200", 50)
        };
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
}
