using System.Linq;
using System.Security;
using System.Security.Cryptography;
using NBitcoin.DataEncoders;

namespace X1.Producer.Domain.Tools
{
    public static class CryptoRandom
    {
        public static byte[] GetRandom(int length)
        {
            var random = new byte[length];

            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(random);
            return random;
        }

        public static void CheckBytes(byte[] bytes, int expectedLength)
        {
            if (bytes == null || bytes.Length != expectedLength || bytes.All(b => b == bytes[0]))
            {
                var display = bytes == null ? "null" : Encoders.Hex.EncodeData(bytes);
                var message =
                    $"Suspicious byte array '{display}', it does not look like a cryptographic key or hash, please investigate. Expected lenght was {expectedLength}.";
                throw new SecurityException(message);
            }
        }
    }
}
