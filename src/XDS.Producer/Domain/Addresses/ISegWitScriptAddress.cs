namespace XDS.Producer.Domain.Addresses
{
    public interface ISegWitScriptAddress
    {
        string RedeemScriptHex { get; }
    }
}