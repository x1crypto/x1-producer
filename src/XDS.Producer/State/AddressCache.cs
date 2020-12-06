using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace XDS.Producer.State
{
    public static class AddressCache
    {
        static readonly object LockObject = new object();
        static readonly Random Random = new Random();

        static BitcoinWitPubKeyAddress[] _addresses;

        public static void SetMineToAddresses(IList<BitcoinWitPubKeyAddress> addresses)
        {
            lock (LockObject)
            {
                _addresses = addresses.ToArray();
            }
        }

        public static BitcoinWitPubKeyAddress[] GetMineToAddresses(int count)
        {
            if (count < 1)
                throw new ArgumentException("Must be > 0.", nameof(count));

            lock (LockObject)
            {
                if (_addresses == null || _addresses.Length == 0)
                    throw new InvalidOperationException("No mine-to address available.");

                var selected = _addresses.OrderBy(x => Random.NextDouble()).Take(count).ToArray();
                if (selected.Length == count)
                    return selected;

                // we have less unique addresses than requested, pick required amount accepting duplicates
                selected = new BitcoinWitPubKeyAddress[count];
                for (int i = 0; i < count; i++)
                {
                    var randomIndex = Random.Next(0, _addresses.Length);
                    selected[i] = _addresses[randomIndex];

                }

                return selected;
            }
        }
    }
}
