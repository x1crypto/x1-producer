using NBitcoin;

namespace XDS.Producer.Domain.Addresses
{
    public sealed class SegWitCoin
    {
        public readonly ISegWitAddress SegWitAddress;

        public readonly uint256 UtxoTxHash;
        public readonly int UtxoTxN;
        public readonly long UtxoValue;
        public readonly UtxoType UtxoType;

        public readonly uint? UtxoPosV3Time;

        public SegWitCoin(ISegWitAddress segWitAddress, uint256 utxoTxHash, int utxoTxN, long utxoValue,
            UtxoType utxoType)
        {
            this.SegWitAddress = segWitAddress;
            this.UtxoTxHash = utxoTxHash;
            this.UtxoTxN = utxoTxN;
            this.UtxoValue = utxoValue;
            this.UtxoType = utxoType;
        }

        public SegWitCoin(ISegWitAddress segWitAddress, uint256 utxoTxHash, int utxoTxN, long utxoValue,
            UtxoType utxoType, uint? utxoPosV3Time) : this(segWitAddress, utxoTxHash, utxoTxN, utxoValue, utxoType)
        {
            this.UtxoPosV3Time = utxoPosV3Time;
        }
    }
}