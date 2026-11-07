namespace LoyaltyPoints.Api.Models;

public class Wallet
{
    public string WalletId { get; }
    public int Balance { get; set; }

    public Wallet(string walletId, int initialBalance)
    {
        WalletId = walletId;
        Balance = initialBalance;
    }
}
