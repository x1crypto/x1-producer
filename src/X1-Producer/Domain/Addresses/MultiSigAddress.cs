using System.Collections.Generic;

namespace X1.Producer.Domain.Addresses
{
    public sealed class MultiSigAddress : ISegWitAddress, ISegWitScriptAddress
    {
        public AddressType AddressType { get; set; }

        public string Address { get; set; }

        public string ScriptPubKeyHex { get; set; }

        public string Label { get; set; }

        public string RedeemScriptHex { get; set; }

        public int? LastSeenHeight { get; set; }

        public KeyMaterial OwnKey { get; set; }

        public int SignaturesRequired { get; set; }

        public int MaxSignatures { get; set; }

        /// <summary>
        /// Key: Compressed public key bytes as lowercase hex string.
        /// Value: Nickname of the owner of the public key for display.
        /// </summary>
        public Dictionary<string, string> OtherPublicKeys { get; set; }

        public byte[] GetEncryptedPrivateKey()
        {
            return this.OwnKey.CipherBytes;
        }


    }
}