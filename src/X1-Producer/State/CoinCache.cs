using System.Collections.Generic;
using System.Linq;
using X1.Producer.Domain.Addresses;

namespace X1.Producer.State
{
    public static class CoinCache
    {
        static readonly object LockObject = new object();

        static SegWitCoin[] _coins;

        public static void ReplaceCoins(List<SegWitCoin> coins)
        {
            lock (LockObject)
            {
                _coins = coins.ToArray();
            }
        }

        public static SegWitCoin[] GetCoinsLocked()
        {
            lock (LockObject)
            {
                if (_coins == null)
                {

                    return new SegWitCoin[0];
                }

                return _coins.ToArray();
            }
        }
    }
}
