namespace X1.Producer.Domain.Addresses
{
    public sealed class PubKeyHashAddress : ISegWitAddress
    {
        public string Address { get; set; }

        public AddressType AddressType { get; set; }

        public string ScriptPubKeyHex { get; set; }

        public string Label { get; set; }

        /// <summary>
        /// This property must only be SET while processing transactions from the blockchain.
        /// The presence of a valid value indicates that the address is a used address.
        /// </summary>
        public int? LastSeenHeight { get; set; }

        public KeyMaterial KeyMaterial;

        public byte[] GetEncryptedPrivateKey()
        {
            return this.KeyMaterial.CipherBytes;
        }


    }
}