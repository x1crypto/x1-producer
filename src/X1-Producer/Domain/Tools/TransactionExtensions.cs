using NBitcoin;

namespace X1.Producer.Domain.Tools
{
    public static class TransactionExtensions
    {
        public static bool IsCoinstake(this Transaction transaction)
        {
            return NBitcoin.Altcoins.X1Crypto.IsCoinstake(transaction);
        }
    }
}
