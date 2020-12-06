using NBitcoin;
using NBitcoin.BouncyCastle.Math;

namespace XDS.Producer.Domain.Tools
{
    public static class TargetExtensions
    {
        public static BigInteger ToBouncyCastleBigInteger(this Target target)
        {
            uint compact = target.ToCompact();
            return ToBouncyCastleBigInteger(compact);
        }

        public static BigInteger ToBouncyCastleBigInteger(this uint compact)
        {
            var exp = compact >> 24;
            var value = compact & 0x00FFFFFF;
            return BigInteger.ValueOf(value).ShiftLeft(8 * ((int)exp - 3));
        }
    }
}
