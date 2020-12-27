namespace X1.Producer.Domain.Addresses
{
    /// <summary>
    /// The AddressType defines what kind of address we are dealing with (e.g. P2WPK or what type
    /// of script address it is. Also, if the underlying key(s) are Hd, the enum values are used to define
    /// the Hd key path. See <see cref="KeyHelper.CreateDerivedPrivateKey"/> for the key path mappings.
    /// </summary>
    public enum AddressType
    {
        MatchAll = -10,

        PubKeyHash = 0,
        MultiSig = 10,
        ColdStakingCold = 30,
        ColdStakingHot = 35
    }
}