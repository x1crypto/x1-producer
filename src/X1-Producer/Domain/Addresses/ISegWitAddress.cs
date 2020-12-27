namespace X1.Producer.Domain.Addresses
{
    public interface ISegWitAddress
    {
        /// <summary>
        /// The Bech32 representation of the address. Unique key of the address across all types of ISegWitAddress for dictionaries and
        /// comparisons. Use ordinal case sensitive comparison. Ensure the string is lowercase.
        /// </summary>
        string Address { get; }
        AddressType AddressType { get; }
        string ScriptPubKeyHex { get; }
        string Label { get; set; }

        byte[] GetEncryptedPrivateKey();

        int? LastSeenHeight { get; set; }
    }
}