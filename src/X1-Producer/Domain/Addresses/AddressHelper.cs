using System;
using System.Linq;
using System.Text;
using NBitcoin;
using NBitcoin.DataEncoders;
using X1.Producer.Domain.Tools;
using X1.Producer.State;

namespace X1.Producer.Domain.Addresses
{
    public static class AddressHelper
    {
        static readonly Bech32Encoder PubKeyAddressEncoder = C.Network.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, true);
        static readonly Bech32Encoder ScriptAddressEncoder = C.Network.GetBech32Encoder(Bech32Type.WITNESS_SCRIPT_ADDRESS, true);
        static readonly string PubKeyAddressPrefix = Encoding.ASCII.GetString(C.Network.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, true).HumanReadablePart) + "1";
        static readonly string ScriptAddressPrefix = Encoding.ASCII.GetString(C.Network.GetBech32Encoder(Bech32Type.WITNESS_SCRIPT_ADDRESS, true).HumanReadablePart) + "1";

        public static Script GetScriptPubKey(this ISegWitAddress address)
        {
            return GetScriptPubKey(address.Address);
        }

        public static Script GetScriptPubKey(this string bech32Address)
        {
            if (bech32Address == null)
                throw new ArgumentNullException(nameof(bech32Address));

            if (bech32Address.Length == C.PubKeyHashAddressLength && bech32Address.StartsWith(PubKeyAddressPrefix))
            {
                var hash160 = PubKeyAddressEncoder.Decode(bech32Address, out var witnessVersion);
                CryptoRandom.CheckBytes(hash160, 20);

                if (witnessVersion != 0)
                    InvalidAddress(bech32Address);

                return new Script(OpcodeType.OP_0, Op.GetPushOp(hash160));
            }

            if (bech32Address.Length == C.ScriptAddressLength && bech32Address.StartsWith(ScriptAddressPrefix))
            {
                var hash256 = PubKeyAddressEncoder.Decode(bech32Address, out var witnessVersion);
                CryptoRandom.CheckBytes(hash256, 32);

                if (witnessVersion != 0)
                    InvalidAddress(bech32Address);

                return new Script(OpcodeType.OP_0, Op.GetPushOp(hash256));
            }

            throw InvalidAddress(bech32Address);
        }



        /// <summary>
        /// This can be the witness commitment in the coinbase transaction or any burn.
        /// </summary>
        public static bool IsOpReturn(this Script scriptPubKey)
        {
            if (scriptPubKey != null)
                return scriptPubKey.Length > 0 && scriptPubKey.ToBytes()[0] == (byte)OpcodeType.OP_RETURN;
            throw InvalidScriptPubKey(null);
        }

        /// <summary>
        /// This can be the non-witness-commitment output in a coinbase transaction.
        /// </summary>
        public static bool IsEmpty(this Script scriptPubKey)
        {
            if (scriptPubKey != null)
                return scriptPubKey.Length == 0;
            throw InvalidScriptPubKey(null);
        }

        /// <summary>
        /// Returns a P2WPKH or P2WSH bech32 string, or throws (it does not return null).
        /// </summary>
        public static string GetAddressFromScriptPubKey(this Script scriptPubKey)
        {
            string address;

            if (scriptPubKey == null || scriptPubKey.Length == 0)
                throw InvalidScriptPubKey(scriptPubKey);

            byte[] raw = scriptPubKey.ToBytes();

            switch (scriptPubKey)
            {
                // P2WPKH
                case var _ when raw.Length == 22 && raw[0] == 0 && raw[1] == 20:
                    var hash160 = raw.Skip(2).Take(20).ToArray();
                    address = hash160.ToPubKeyHashAddress();
                    break;
                // P2WSH
                case var _ when raw.Length == 34 && raw[0] == 0 && raw[1] == 32:
                    var hash256 = raw.Skip(2).Take(32).ToArray();
                    address = hash256.ToScriptAddress();
                    break;
                // ignore everything else and return null
                default:
                    return null;
            }

            return address;
        }

        public static bool IsProtocolOutput(this TxOut txOut, Transaction transaction)
        {
            if (transaction.IsCoinBase)
            {
                if (txOut.ScriptPubKey.IsEmpty())
                    return true; // in a PoS block the first output of the coinstake tx is empty
                if (txOut.ScriptPubKey.IsOpReturn())
                    return true; // witness commitment
            }

            if (transaction.IsCoinstake())
            {
                if (txOut.ScriptPubKey.IsEmpty())
                    return true; // this normally the empty first output (PoS marker)
                if (txOut.ScriptPubKey.IsOpReturn())
                    return true; // this is the public key, at index 1 in a coinstake tx
            }

            return false;
        }

        public static string ToPubKeyHashAddress(this byte[] hash160)
        {
            CryptoRandom.CheckBytes(hash160, 20);

            return PubKeyAddressEncoder.Encode(0, hash160);
        }

        public static string ToScriptAddress(this byte[] hash256)
        {
            CryptoRandom.CheckBytes(hash256, 32);

            return ScriptAddressEncoder.Encode(0, hash256);
        }

        public static ISegWitAddress Match(this ISegWitAddress segWitAddress, string address = null, AddressType addressType = AddressType.MatchAll)
        {
            if (segWitAddress == null)  // no op
                return null;

            if (address != null) // filter by address
            {
                if (segWitAddress.Address == address)
                {
                    if (addressType == AddressType.MatchAll || addressType == segWitAddress.AddressType)
                        return segWitAddress;
                    return null;
                }
                return null;
            }

            // do not filter by address
            if (addressType == AddressType.MatchAll)
                return segWitAddress;

            if (addressType == segWitAddress.AddressType)
                return segWitAddress;

            return null;
        }

        public static Script GetRedeemScript(this ISegWitScriptAddress segWitScriptAddress)
        {
            return new Script(Encoders.Hex.DecodeData(segWitScriptAddress.RedeemScriptHex));
        }

        public static Coin ToCoin(this SegWitCoin segWitCoin)
        {
            var outpoint = new OutPoint(segWitCoin.UtxoTxHash, segWitCoin.UtxoTxN);
            var txOut = new TxOut(segWitCoin.UtxoValue, segWitCoin.SegWitAddress.GetScriptPubKey());
            var coin = new Coin(outpoint, txOut);
            return coin;
        }

        public static Key GetPrivateKey(this SegWitCoin coin, string passphrase)
        {
            var adr = (PubKeyHashAddress)coin.SegWitAddress;
            return new Key(adr.KeyMaterial.PlaintextBytes);
        }

        static InvalidOperationException InvalidAddress(string input, Exception innerException = null)
        {
            var message = $"Invalid address '{input ?? "null"}'.";
            return new InvalidOperationException(message, innerException);
        }

        static InvalidOperationException InvalidScriptPubKey(Script input, Exception innerException = null)
        {
            var message = $"Invalid ScriptPubKey '{input?.ToString() ?? "null"}'.";
            return new InvalidOperationException(message, innerException);
        }
    }
}
