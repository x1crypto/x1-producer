using System;
using System.Diagnostics;
using NBitcoin;
using NBitcoin.Crypto;
using XDS.Producer.Domain.Addresses;
using XDS.Producer.State;
using static NBitcoin.OpcodeType;

namespace XDS.Producer.Staking
{
    public static class SigningService
    {
        public static void SignInputs(Transaction transaction, Key[] keys, SegWitCoin[] coins)
        {
            for (var i = 0; i < transaction.Inputs.Count; i++)
            {
                var txin = transaction.Inputs[i];
                var key = keys[i];
                var coin = coins[i];
                SignInput(txin, key, coin, i, transaction);
            }
        }

        static void SignInput(TxIn txin, Key key, SegWitCoin coin, int index, Transaction transaction)
        {
            if (coin.SegWitAddress.AddressType == AddressType.PubKeyHash)
            {
                Script scriptCode = GetScriptCode(coin.SegWitAddress.GetScriptPubKey());
                uint256 signatureHash = GetHashToSign(transaction, index, scriptCode, coin.UtxoValue);
                byte[] finalSig = GetSignature(signatureHash, key);
                txin.WitScript = new WitScript(Op.GetPushOp(finalSig), Op.GetPushOp(key.PubKey.Compress().ToBytes()));
            }
            else if (coin.SegWitAddress is ColdStakingAddress coldStakingAddress)
            {
                Script scriptCode = coldStakingAddress.GetRedeemScript();
                uint256 signatureHash = GetHashToSign(transaction, index, scriptCode, coin.UtxoValue);
                byte[] finalSig = GetSignature(signatureHash, key);

                var isColdPubKey = coldStakingAddress.AddressType == AddressType.ColdStakingCold;
                var publicKey = key.PubKey.Compress();
                var scriptSig = new Script(Op.GetPushOp(finalSig), isColdPubKey ? OP_0 : OP_1, Op.GetPushOp(publicKey.ToBytes()));
                txin.WitScript = scriptSig + new WitScript(Op.GetPushOp(coldStakingAddress.GetRedeemScript().ToBytes(true)));
            }
            else if (coin.SegWitAddress is MultiSigAddress multiSigAddress)
            {
                var scriptCoin = coin.ToCoin().ToScriptCoin(multiSigAddress.GetRedeemScript());
                var bitcoinSecret = new BitcoinSecret(key,C.Network);
                transaction.Sign(new[] { bitcoinSecret }, new[] { scriptCoin });
                //transaction.Sign(C.Network, new[] { key }, new[] { scriptCoin });
            }
            else
                throw new NotSupportedException();
        }

        static byte[] GetSignature(uint256 hashToSign, Key key)
        {
            var signature = key.Sign(hashToSign, SigHash.All);
            ECDSASignature ecdsaSig = signature.Signature;
            byte[] derSig = ecdsaSig.ToDER();
            byte[] finalSig = new byte[derSig.Length + 1];
            Array.Copy(derSig, 0, finalSig, 0, derSig.Length);
            finalSig[finalSig.Length - 1] = (byte)SigHash.All;
            return finalSig;
        }

        static uint256 GetHashToSign(Transaction tx, int index, Script scriptCode, long amount)
        {
            const SigHash sigHash = SigHash.All;

            uint256 hashPrevouts = GetHashPrevouts(tx);
            uint256 hashSequence = GetHashSequence(tx);
            uint256 hashOutputs = GetHashOutputs(tx);

            BitcoinStream stream = CreateHashWriter(HashVersion.WitnessV0);

            stream.ReadWrite(tx.Version);
            // Input prevouts/nSequence (none/all, depending on flags)
            stream.ReadWrite(hashPrevouts);
            stream.ReadWrite(hashSequence);
            // The input being signed (replacing the scriptSig with scriptCode + amount)
            // The prevout may already be contained in hashPrevout, and the nSequence
            // may already be contain in hashSequence.
            stream.ReadWrite(tx.Inputs[index].PrevOut);
            stream.ReadWrite(scriptCode);
            stream.ReadWrite(amount);
            // ReSharper disable once RedundantCast
            stream.ReadWrite((uint)tx.Inputs[index].Sequence);
            // Outputs (none/one/all, depending on flags)
            stream.ReadWrite(hashOutputs);
            // Locktime
            stream.ReadWriteStruct(tx.LockTime);
            // Sighash type
            stream.ReadWrite((uint)sigHash);

            return GetHash(stream);
        }

        static Script GetScriptCode(Script scriptPubKey)
        {
            WitKeyId key = PayToWitPubKeyHashExtractScriptPubKeyParameters(scriptPubKey);
            KeyId keyId = key.AsKeyId();
            var scriptCode = keyId.ScriptPubKey;
            Debug.Assert(scriptPubKey != scriptCode);
            return scriptCode;
        }

        static WitKeyId PayToWitPubKeyHashExtractScriptPubKeyParameters(Script scriptPubKey)
        {
            var data = new byte[20];
            Array.Copy(scriptPubKey.ToBytes(true), 2, data, 0, 20);
            return new WitKeyId(data);
        }

        static uint256 GetHashPrevouts(Transaction txTo)
        {
            uint256 hashPrevouts;
            BitcoinStream ss = CreateHashWriter(HashVersion.WitnessV0);
            foreach (TxIn input in txTo.Inputs)
            {
                ss.ReadWrite(input.PrevOut);
            }
            hashPrevouts = GetHash(ss);
            return hashPrevouts;
        }

        static uint256 GetHashOutputs(Transaction txTo)
        {
            uint256 hashOutputs;
            BitcoinStream ss = CreateHashWriter(HashVersion.WitnessV0);
            foreach (TxOut txout in txTo.Outputs)
            {
                ss.ReadWrite(txout);
            }
            hashOutputs = GetHash(ss);
            return hashOutputs;
        }

        static uint256 GetHashSequence(Transaction txTo)
        {
            uint256 hashSequence;
            BitcoinStream ss = CreateHashWriter(HashVersion.WitnessV0);
            foreach (TxIn input in txTo.Inputs)
            {
                // ReSharper disable once RedundantCast
                ss.ReadWrite((uint)input.Sequence);
            }
            hashSequence = GetHash(ss);
            return hashSequence;
        }

        static BitcoinStream CreateHashWriter(HashVersion version)
        {
            var hs = new HashStream();
            var stream = new BitcoinStream(hs, true)
            {
                Type = SerializationType.Hash,
                TransactionOptions = version == HashVersion.Original
                    ? TransactionOptions.None
                    : TransactionOptions.Witness
            };
            return stream;
        }

        static uint256 GetHash(BitcoinStream stream)
        {
            uint256 preimage = ((HashStream)stream.Inner).GetHash();
            stream.Inner.Dispose();
            return preimage;
        }

    }
}
