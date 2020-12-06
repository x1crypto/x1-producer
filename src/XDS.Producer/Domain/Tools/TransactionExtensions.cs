using NBitcoin;

namespace XDS.Producer.Domain.Tools
{
    public static class TransactionExtensions
    {
        public static bool IsCoinstake(this Transaction transaction)
        {
            return NBitcoin.Altcoins.XDS.IsCoinstake(transaction);
        }
    }
}
