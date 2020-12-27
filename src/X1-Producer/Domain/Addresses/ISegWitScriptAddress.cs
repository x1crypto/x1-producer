namespace X1.Producer.Domain.Addresses
{
    public interface ISegWitScriptAddress
    {
        string RedeemScriptHex { get; }
    }
}