namespace XDS.Producer.Domain.Addresses
{
    public sealed class KeyMaterial
    {
        public KeyType KeyType;

        public string KeyPath;

        public int? AddressIndex;

        public int? IsChange;

        public long CreatedUtc;

        public byte[] CipherBytes;

        /// <summary>
        /// This field must only used in a wallet dump;
        /// </summary>
        public byte[] PlaintextBytes;

        public KeyMaterial Clone()
        {
            return new KeyMaterial
            {
                AddressIndex = this.AddressIndex,
                CipherBytes = this.CipherBytes,
                CreatedUtc = this.CreatedUtc,
                IsChange = this.IsChange,
                KeyPath = this.KeyPath,
                KeyType = this.KeyType,
                PlaintextBytes = null // do not include this
            };
        }
    }
}
