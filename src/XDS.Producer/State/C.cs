using NBitcoin;
using XDS.Producer.Domain.Addresses;
using XDS.Producer.Domain.Tools;

namespace XDS.Producer.State
{
    public static class C
    {
        /// <summary>
        /// A static Network instance for use in static (extension) methods.
        /// </summary>
        public static Network Network;

        /// <summary>
        /// ProtocolVersion 70013.
        /// </summary>
        public static uint ProtocolVersion = 70013;

        /// <summary>
        /// A coin has 100_000_000 Satoshis.
        /// </summary>
        public const long SatoshisPerCoin = 100_000_000;

        /// <summary>
        /// Length of a bech32 PubKeyHash address.
        /// </summary>
        public static int PubKeyHashAddressLength => CryptoRandom.GetRandom(20).ToPubKeyHashAddress().Length;

        /// <summary>
        /// Length of a bech32 Script address.
        /// </summary>
        public static int ScriptAddressLength => CryptoRandom.GetRandom(32).ToScriptAddress().Length;
    }
}
