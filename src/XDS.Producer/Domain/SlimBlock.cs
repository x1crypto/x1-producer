using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NBitcoin;
using NBitcoin.Crypto;
using XDS.Producer.Domain.Tools;
using XDS.Producer.State;

namespace XDS.Producer.Domain
{
    public class SlimBlock
    {
        public bool IsProofOfStake;
        public SlimBlockHeader SlimBlockHeader;
        public Transaction CoinbaseTransaction;
        public Transaction CoinstakeTransaction;
        public List<Transaction> PayloadTransactions;
        public int Height;
        public byte[] SignatureBytes;
    }


    public static class SlimBlockExtensions
    {
        // The wtxid of the coinbase transaction is defined as to be 0x0000....0000.
        static readonly Script CoinbaseWitScript = new WitScript(Op.GetPushOp(new byte[32]));

        public static void AddPoWCoinbaseTransaction(this SlimBlock slimBlock, Script scriptPubkey, Money value, long extraNonce)
        {
            if (slimBlock.IsProofOfStake || slimBlock.Height == 0)
                throw new InvalidOperationException();

            // txCoinbase.vin[0].scriptSig = (CScript() << nHeight << CScriptNum(nExtraNonce));
            var heightScriptBytes = new Script(Op.GetPushOp(slimBlock.Height)).ToBytes();
            var extraNonceScriptBytes = new CScriptNum(extraNonce).getvch();
            var scriptSigBytes = new byte[heightScriptBytes.Length + extraNonceScriptBytes.Length];
            Buffer.BlockCopy(heightScriptBytes, 0, scriptSigBytes, 0, heightScriptBytes.Length);
            Buffer.BlockCopy(extraNonceScriptBytes, 0, scriptSigBytes, heightScriptBytes.Length, extraNonceScriptBytes.Length);

            slimBlock.CoinbaseTransaction = C.Network.CreateTransaction();
            slimBlock.CoinbaseTransaction.Inputs.Add(new TxIn
                {ScriptSig = new Script(scriptSigBytes), WitScript = CoinbaseWitScript});
            slimBlock.CoinbaseTransaction.Outputs.Add(new TxOut(value, scriptPubkey));
        }

        public static void AddPoSCoinbaseTransaction(this SlimBlock slimBlock)
        {
            slimBlock.CoinbaseTransaction = C.Network.CreateTransaction();
            slimBlock.CoinbaseTransaction.Inputs.Add(new TxIn
            {
                ScriptSig = new Script(Op.GetPushOp(slimBlock.Height)) + OpcodeType.OP_0, WitScript = CoinbaseWitScript
            });
            slimBlock.CoinbaseTransaction.Outputs.Add(new TxOut(0, new Script()));
        }

        public static void SetPayloadTransactions(this SlimBlock slimBlock, IEnumerable<Transaction> transactions)
        {
            slimBlock.PayloadTransactions = transactions.ToList();
        }

        public static void CreateWitnessCommitment(this SlimBlock slimBlock)
        {
            if (slimBlock.CoinbaseTransaction == null || slimBlock.PayloadTransactions == null)
                throw new ArgumentException();

            if (slimBlock.IsProofOfStake && slimBlock.CoinstakeTransaction == null)
                throw new ArgumentException();

            if (slimBlock.CoinbaseTransaction.Inputs[0].WitScript == WitScript.Empty)
                throw new ArgumentException();

            byte[] witnessRootHash;
            if (slimBlock.IsProofOfStake)
            {
                List<Transaction> coinStakeAndPayload = new List<Transaction>(slimBlock.PayloadTransactions);
                coinStakeAndPayload.Insert(0, slimBlock.CoinstakeTransaction);
                witnessRootHash = BlockWitnessMerkleRoot(coinStakeAndPayload);
            }
            else
            {
                witnessRootHash = BlockWitnessMerkleRoot(slimBlock.PayloadTransactions);
            }

            // // Coinbase's input's witness must consist of a single 32-byte array for the witness reserved value.
            byte[] witnessReservedValue = new byte[32];

            byte[] dataToHash = new byte[64]; // witness root hash << witness reserved value
            Buffer.BlockCopy(witnessRootHash, 0, dataToHash, 0, 32);
            Buffer.BlockCopy(witnessReservedValue, 0, dataToHash, 32, 32);

            // 32-byte - Commitment hash: Double-SHA256(witness root hash|witness reserved value)
            byte[] commitmentHash = Hashes.DoubleSHA256(dataToHash).ToBytes();

            // The commitment is recorded in a scriptPubKey of the coinbase transaction.
            var coinbaseScriptPubKeyFiledBytes = new byte[38];   // It must be at least 38 bytes, with the first 6-byte of 0x6a24aa21a9ed.
            coinbaseScriptPubKeyFiledBytes[0] = 0x6a;            // OP_RETURN (0x6a)
            coinbaseScriptPubKeyFiledBytes[1] = 0x24;            // Push the following 36 bytes (0x24)
            coinbaseScriptPubKeyFiledBytes[2] = 0xaa;            // Commitment header (0xaa21a9ed)
            coinbaseScriptPubKeyFiledBytes[3] = 0x21;
            coinbaseScriptPubKeyFiledBytes[4] = 0xa9;
            coinbaseScriptPubKeyFiledBytes[5] = 0xed;
            Buffer.BlockCopy(commitmentHash, 0, coinbaseScriptPubKeyFiledBytes, 6, 32);

            // Write the coinbase commitment to a ScriptPubKey structure.
            var txOut = new TxOut(Money.Zero, new Script(coinbaseScriptPubKeyFiledBytes));

            // If there are more than one scriptPubKey matching the pattern, the one with highest output index is assumed to be the commitment.
            slimBlock.CoinbaseTransaction.Outputs.Add(txOut);
        }

        public static void UpdateHeaderHashMerkleRoot(this SlimBlock slimBlock)
        {
            var txHashes = new List<byte[]>();
            txHashes.Add(slimBlock.CoinbaseTransaction.GetHash().ToBytes());
            if (slimBlock.IsProofOfStake)
                txHashes.Add(slimBlock.CoinstakeTransaction.GetHash().ToBytes());
            foreach (var transaction in slimBlock.PayloadTransactions)
                txHashes.Add(transaction.GetHash().ToBytes());
            var hashMerkleRoot = MerkleRoot.Build(txHashes);
            slimBlock.SlimBlockHeader.MerkleRoot = hashMerkleRoot;
            slimBlock.SlimBlockHeader.Data = slimBlock.SlimBlockHeader.SerializeTo80Bytes();
        }

        static byte[] BlockWitnessMerkleRoot(List<Transaction> payloadTransactions)
        {
            var leaves = new List<byte[]>();
            // The witness hash of the coinbase transaction is 0.
            leaves.Add(new byte[32]);

            foreach (Transaction tx in payloadTransactions)
                leaves.Add(tx.GetWitHash().ToBytes());

            // A witness root hash is calculated with all those wtxid as leaves, in a way similar to the hashMerkleRoot in the block header.
            return MerkleRoot.Build(leaves);
        }

        public static byte[] Serialize(this SlimBlock slimBlock)
        {

            // These buffers will be concatenated to create the full block;
            List<byte[]> parts = new List<byte[]>();

            // Block header
            parts.Add(slimBlock.SlimBlockHeader.Data);

            // Number of tx in the block
            byte[] nTransactions = new byte[1];

            // this must be VarInt, todo: use real varint
            var txCount = slimBlock.IsProofOfStake ? slimBlock.PayloadTransactions.Count + 2 : slimBlock.PayloadTransactions.Count + 1;
            if (txCount > 252)
                throw new InvalidOperationException($"Value of {txCount} needs a real VarInt");
            nTransactions[0] = (byte)txCount;
            parts.Add(nTransactions);

            // Transactions
            var coinbaseTxBytes = slimBlock.CoinbaseTransaction.ToBytes();
            parts.Add(coinbaseTxBytes);

            if (slimBlock.IsProofOfStake)
            {
                Debug.Assert(slimBlock.CoinstakeTransaction.IsCoinstake());
                var coinstakeTxBytes = slimBlock.CoinstakeTransaction.ToBytes();
                parts.Add(coinstakeTxBytes);
            }

            foreach (var payloadTransaction in slimBlock.PayloadTransactions)
            {
                parts.Add(payloadTransaction.ToBytes());
            }

            // Block signature as VarString. First byte of VarString is length as VarInt
            if (slimBlock.IsProofOfStake)
            {
                // 252 (0xfc) is the max for a 1-byte VarInt, and we assume the signature is not longer
                Debug.Assert(slimBlock.SignatureBytes.Length <= 252);
                parts.Add(slimBlock.SignatureBytes); // SignatureBytes is written by the BlockSignature ReadWrite method. This should already be in VarString format.
                //byte[] signature = new byte[1 + slimBlock.SignatureBytes.Length];
                //signature[0] = (byte)slimBlock.SignatureBytes.Length;
                //Buffer.BlockCopy(slimBlock.SignatureBytes, 0, signature, 1, slimBlock.SignatureBytes.Length);
                //parts.Add(signature);
            }
            else
            {

                parts.Add(slimBlock.SignatureBytes);
            }

            var blockBytes = ByteArrays.Concatenate(parts.ToArray());
            return blockBytes;
        }
    }
}
