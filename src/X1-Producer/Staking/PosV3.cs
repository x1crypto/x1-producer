using NBitcoin;
using NBitcoin.BouncyCastle.Math;

namespace X1.Producer.Staking
{
    public sealed class PosV3
    {
        public long CurrentBlockTime;  // used to calculate kernel

        public int SearchInterval; // used to calculate CurrentBlockTime
        public int BlockInterval;  // used to calculate expected time

        internal BigInteger TargetAsBigInteger; // used to calculate kernel (internal so that it doesn't get serialized)

        public uint256 StakeModifierV2; // used to calculate kernel
        public uint256 Target; // same as TargetAsBigInteger for display only
        public double TargetDifficulty;  // same as TargetAsBigInteger for display, expected time, network weight
    }
}
