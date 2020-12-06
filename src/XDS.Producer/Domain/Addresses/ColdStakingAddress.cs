namespace XDS.Producer.Domain.Addresses
{
    public class ColdStakingAddress : ISegWitAddress, ISegWitScriptAddress
    {
        public string Address { get; set; }

        public AddressType AddressType { get; set; }

        public string ScriptPubKeyHex { get; set; }

        public string Label { get; set; }

        public string RedeemScriptHex { get; set; }

        public int? LastSeenHeight { get; set; }

        public KeyMaterial ColdKey { get; set; }

        public KeyMaterial HotKey { get; set; }

        public byte[] StakingKey { get; set; }

        public byte[] GetEncryptedPrivateKey()
        {
            if (this.AddressType == AddressType.ColdStakingCold)
                return this.ColdKey.CipherBytes;
            return this.HotKey.CipherBytes;
        }

        public ColdStakingAddress Clone()
        {
            return new ColdStakingAddress
            {
                AddressType = this.AddressType,
                Address = this.Address,
                ColdKey = this.ColdKey.Clone(),
                HotKey = this.HotKey.Clone(),
                Label = this.Label,
                LastSeenHeight = this.LastSeenHeight,
                RedeemScriptHex = this.RedeemScriptHex,
                ScriptPubKeyHex = this.ScriptPubKeyHex,
                StakingKey = this.StakingKey

            };
        }
    }
}