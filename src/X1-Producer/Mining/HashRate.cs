using System;
using NBitcoin;

namespace X1.Producer.Mining
{
    /// <summary>
    /// Hash rate calculations.
    /// </summary>
    public static class HashRate
    {
        /// <summary>
        /// Get the actual hash rate in MHash/s.
        /// </summary>
        /// <param name="startNonce">Start nonce</param>
        /// <param name="endNonce">End Nonce</param>
        /// <param name="elapsedMs">Elapsed time in ms</param>
        /// <returns>Hash rate in MHash/s</returns>
        public static double GetMHashPerSecond(uint startNonce, uint endNonce, double elapsedMs)
        {
            double hashes = endNonce - startNonce;
            if (elapsedMs <= 0 || hashes <= 0)
                return 0;

            return hashes / elapsedMs / 1_000.0;
        }

        /// <summary>
        /// Hashes = D * 2**32 / seconds_between_blocks
        /// https://en.bitcoin.it/wiki/Difficulty
        /// </summary>
        /// <param name="bits">Target bits</param>
        /// <returns>Estimated network hash rate in GHash/s</returns>
        public static double EstimateGHashPerSecondFromBits(uint bits)
        {
            var targetSpacingSeconds = 256.0;

            var target = new Target(bits);
            var difficulty = target.Difficulty;
            var gigaHashPerSecond = difficulty * Math.Pow(2, 32) / targetSpacingSeconds / 1_000_000_000.0;
            return gigaHashPerSecond;
        }
    }
}
