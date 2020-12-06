using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using XDS.Producer.Domain.Tools;
using XDS.Producer.State;

namespace XDS.Producer.Domain.RPC.GetBlockTemplate
{
    public static class RPCBlockTemplateExtensions
    {
        public static SlimBlock CreateTemplateBlock(this RPCBlockTemplate blockTemplate, uint time, bool isProofOfStake, Script scriptPubKey, Transaction coinstakeTransaction, Key blockSignatureKey)
        {
            if (!isProofOfStake && (scriptPubKey == null || coinstakeTransaction != null)
                || isProofOfStake && (scriptPubKey != null || coinstakeTransaction == null))
                throw new InvalidOperationException("Pow/pos parameter mismatch.");

            var slimBlockHeader = blockTemplate.CreateSlimBlockHeader(isProofOfStake);
            slimBlockHeader.Timestamp = time;

            var slimBlock = new SlimBlock { IsProofOfStake = isProofOfStake, Height = blockTemplate.height, SlimBlockHeader = slimBlockHeader };
            if (isProofOfStake)
            {
                slimBlock.AddPoSCoinbaseTransaction();
                slimBlock.CoinstakeTransaction = coinstakeTransaction;
            }
            else
                slimBlock.AddPoWCoinbaseTransaction(scriptPubKey, blockTemplate.coinbasevalue, blockTemplate.ExtraNonce);

            slimBlock.SetPayloadTransactions(blockTemplate.transactions.Select(x =>
            {
                var tx = C.Network.CreateTransaction();
                tx.FromBytes(Encoders.Hex.DecodeData(x.data), C.ProtocolVersion);

                if (tx.GetHash() != uint256.Parse(x.txid))
                    throw new InvalidOperationException();

                return tx;
            }));
            slimBlock.CreateWitnessCommitment();
            slimBlock.UpdateHeaderHashMerkleRoot();
            slimBlock.SignBlock(blockSignatureKey);
            slimBlock.ValidateWitnessCommitment();
            return slimBlock;
        }

        public static void SignBlock(this SlimBlock slimBlock, Key blockSignatureKey)
        {
            if (slimBlock.IsProofOfStake)
            {
                var headerBytes = slimBlock.SlimBlockHeader.SerializeTo80Bytes();
                slimBlock.SlimBlockHeader.Data = headerBytes; // the Data field is used in serialization

                var headerHash = Hashes.DoubleSHA256(headerBytes);
                ECDSASignature signature = blockSignatureKey.Sign(headerHash);
                var blockSignature = new NBitcoin.Altcoins.XDS.XDSBlockSignature { Signature = signature.ToDER() };
                slimBlock.SignatureBytes = blockSignature.ToBytes();
            }
            else
            {
                slimBlock.SignatureBytes = new[] { (byte)0 }; // VarString with length of zero, zero encoded as as VarInt
            }

        }

        public static void ValidateWitnessCommitment(this SlimBlock slimBlock)
        {
            var ser = slimBlock.Serialize();
            Block block = Block.Load(ser, C.Network.Consensus.ConsensusFactory);
            if (!ByteArrays.AreAllBytesEqual(ser, block.ToBytes(C.ProtocolVersion)))
                throw new InvalidOperationException("Serialization issue.");

            // Validation for witness commitments.
            // * We compute the witness hash (which is the hash including witnesses) of all the block's transactions, except the
            //   coinbase (where 0x0000....0000 is used instead).
            // * The coinbase scriptWitness is a stack of a single 32-byte vector, containing a witness nonce (unconstrained).
            // * We build a merkle tree with all those witness hashes as leaves (similar to the hashMerkleRoot in the block header).
            // * There must be at least one output whose scriptPubKey is a single 36-byte push, the first 4 bytes of which are
            //   {0xaa, 0x21, 0xa9, 0xed}, and the following 32 bytes are SHA256^2(witness root, witness nonce). In case there are
            //   multiple, the last one is used.
            bool fHaveWitness = false;

            Script commitment = GetWitnessCommitment(block);
            if (commitment != null)
            {
                uint256 hashWitness = BlockWitnessMerkleRoot(block, out bool _);

                // The malleation check is ignored; as the transaction tree itself
                // already does not permit it, it is impossible to trigger in the
                // witness tree.
                WitScript witness = block.Transactions[0].Inputs[0].WitScript;
                if ((witness.PushCount != 1) || (witness.Pushes.First().Length != 32))
                {

                    throw new InvalidOperationException("ConsensusErrors.BadWitnessNonceSize");
                }

                var hashed = new byte[64];
                Buffer.BlockCopy(hashWitness.ToBytes(), 0, hashed, 0, 32);
                Buffer.BlockCopy(witness.Pushes.First(), 0, hashed, 32, 32);
                hashWitness = Hashes.DoubleSHA256(hashed); // todo: not nice code to reuse tha variable


                if (!ByteArrays.AreAllBytesEqual(hashWitness.ToBytes(), commitment.ToBytes(true).Skip(6).ToArray()))
                {
                    throw new InvalidOperationException("ConsensusErrors.BadWitnessMerkleMatch");
                }

                fHaveWitness = true;
            }

            if (!fHaveWitness)
            {
                for (int i = 0; i < block.Transactions.Count; i++)
                {
                    if (block.Transactions[i].HasWitness)
                    {
                        throw new InvalidOperationException("Unexpected witness.");
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the Merkle-root for witness data.
        /// </summary>
        /// <param name="block">Block which transactions witness data is used for calculation.</param>
        /// <param name="mutated"><c>true</c> if at least one leaf of the merkle tree has the same hash as any subtree. Otherwise: <c>false</c>.</param>
        /// <returns>Merkle root.</returns>
        public static uint256 BlockWitnessMerkleRoot(Block block, out bool mutated)
        {
            var leaves = new List<uint256> { uint256.Zero }; // The witness hash of the coinbase is 0.

            foreach (Transaction tx in block.Transactions.Skip(1))
                leaves.Add(tx.GetWitHash());

            return ComputeMerkleRoot(leaves, out mutated);

        }

        /// <summary>
        /// Computes the Merkle-root.
        /// </summary>
        /// <remarks>This implements a constant-space merkle root/path calculator, limited to 2^32 leaves.</remarks>
        /// <param name="leaves">Merkle tree leaves.</param>
        /// <param name="mutated"><c>true</c> if at least one leaf of the merkle tree has the same hash as any subtree. Otherwise: <c>false</c>.</param>
        public static uint256 ComputeMerkleRoot(List<uint256> leaves, out bool mutated)
        {
            mutated = false;
            if (leaves.Count == 0) return uint256.Zero;

            var branch = new List<uint256>();

            // subTreeHashes is an array of eagerly computed subtree hashes, indexed by tree
            // level (0 being the leaves).
            // For example, when count is 25 (11001 in binary), subTreeHashes[4] is the hash of
            // the first 16 leaves, subTreeHashes[3] of the next 8 leaves, and subTreeHashes[0] equal to
            // the last leaf. The other subTreeHashes entries are undefined.
            var subTreeHashes = new uint256[32];

            for (int i = 0; i < subTreeHashes.Length; i++)
                subTreeHashes[i] = uint256.Zero;

            // Which position in inner is a hash that depends on the matching leaf.
            int matchLevel = -1;
            uint processedLeavesCount = 0;
            var hash = new byte[64];

            // First process all leaves into subTreeHashes values.
            while (processedLeavesCount < leaves.Count)
            {
                uint256 currentLeaveHash = leaves[(int)processedLeavesCount];
                bool match = false;
                processedLeavesCount++;
                int level;

                // For each of the lower bits in processedLeavesCount that are 0, do 1 step. Each
                // corresponds to an subTreeHash value that existed before processing the
                // current leaf, and each needs a hash to combine it.
                for (level = 0; (processedLeavesCount & (((uint)1) << level)) == 0; level++)
                {
                    if (match)
                    {
                        branch.Add(subTreeHashes[level]);
                    }
                    else if (matchLevel == level)
                    {
                        branch.Add(currentLeaveHash);
                        match = true;
                    }
                    if (!mutated)
                        mutated = subTreeHashes[level] == currentLeaveHash;

                    Buffer.BlockCopy(subTreeHashes[level].ToBytes(), 0, hash, 0, 32);
                    Buffer.BlockCopy(currentLeaveHash.ToBytes(), 0, hash, 32, 32);
                    currentLeaveHash = Hashes.DoubleSHA256(hash);
                }

                // Store the resulting hash at subTreeHashes position level.
                subTreeHashes[level] = currentLeaveHash;
                if (match)
                    matchLevel = level;
            }

            uint256 root;

            {
                // Do a final 'sweep' over the rightmost branch of the tree to process
                // odd levels, and reduce everything to a single top value.
                // Level is the level (counted from the bottom) up to which we've sweeped.
                int level = 0;

                // As long as bit number level in processedLeavesCount is zero, skip it. It means there
                // is nothing left at this level.
                while ((processedLeavesCount & (((uint)1) << level)) == 0)
                    level++;

                root = subTreeHashes[level];
                bool match = matchLevel == level;
                var hashh = new byte[64];

                while (processedLeavesCount != (((uint)1) << level))
                {
                    // If we reach this point, hash is a subTreeHashes value that is not the top.
                    // We combine it with itself (Bitcoin's special rule for odd levels in
                    // the tree) to produce a higher level one.
                    if (match)
                        branch.Add(root);

                    // Line was added to allocate once and not twice
                    var rootBytes = root.ToBytes();
                    Buffer.BlockCopy(rootBytes, 0, hash, 0, 32);
                    Buffer.BlockCopy(rootBytes, 0, hash, 32, 32);
                    root = Hashes.DoubleSHA256(hash);

                    // Increment processedLeavesCount to the value it would have if two entries at this
                    // level had existed.
                    processedLeavesCount += (((uint)1) << level);
                    level++;

                    // And propagate the result upwards accordingly.
                    while ((processedLeavesCount & (((uint)1) << level)) == 0)
                    {
                        if (match)
                        {
                            branch.Add(subTreeHashes[level]);
                        }
                        else if (matchLevel == level)
                        {
                            branch.Add(root);
                            match = true;
                        }

                        Buffer.BlockCopy(subTreeHashes[level].ToBytes(), 0, hashh, 0, 32);
                        Buffer.BlockCopy(root.ToBytes(), 0, hashh, 32, 32);
                        root = Hashes.DoubleSHA256(hashh);

                        level++;
                    }
                }
            }

            return root;
        }

        public static Script GetWitnessCommitment(Block block)
        {
            Script commitScriptPubKey = null;

            for (int i = 0; i < block.Transactions[0].Outputs.Count; i++)
            {
                Script scriptPubKey = block.Transactions[0].Outputs[i].ScriptPubKey;

                if (IsWitnessScript(scriptPubKey))
                {
                    commitScriptPubKey = scriptPubKey;
                }
            }

            return commitScriptPubKey;
        }

        private static bool IsWitnessScript(Script script)
        {
            if (script.Length >= 38)
            {
                byte[] scriptBytes = script.ToBytes(true);

                if ((scriptBytes[0] == (byte)OpcodeType.OP_RETURN) &&
                    (scriptBytes[1] == 0x24) &&
                    (scriptBytes[2] == 0xaa) &&
                    (scriptBytes[3] == 0x21) &&
                    (scriptBytes[4] == 0xa9) &&
                    (scriptBytes[5] == 0xed))
                {
                    return true;
                }
            }

            return false;
        }

        public static int GetCurrentTipHeight(this RPCBlockTemplate blockTemplate)
        {
            return blockTemplate.height - 1;
        }

        public static RPCBlockTemplate Clone(this RPCBlockTemplate blockTemplate)
        {
            var clone = new RPCBlockTemplate
            {
                height = blockTemplate.height,
                longpollid = blockTemplate.longpollid,
                transactions = blockTemplate.transactions,
                stakemodifierv2 = blockTemplate.stakemodifierv2,
                coinbasevalue = blockTemplate.coinbasevalue,
                bits = blockTemplate.bits,
                posbits = blockTemplate.posbits,
                previousblockhash = blockTemplate.previousblockhash,
                version = blockTemplate.version,
                mintime = blockTemplate.mintime,
                capabilities = blockTemplate.capabilities,
                curtime = blockTemplate.curtime,
                default_witness_commitment = blockTemplate.default_witness_commitment,
                mutable = blockTemplate.mutable,
                postarget = blockTemplate.postarget,
                previousblockconsensus = blockTemplate.previousblockconsensus,
                previousblocktime = blockTemplate.previousblocktime,
                target = blockTemplate.target,
                rules = blockTemplate.rules
            };

            List<RPCBlockTemplateTransaction> ttx = new List<RPCBlockTemplateTransaction>();
            foreach (var tx in blockTemplate.transactions)
            {
                ttx.Add(new RPCBlockTemplateTransaction
                {
                    txid = tx.txid,
                    data = tx.data,
                    depends = tx.depends,
                    fee = tx.fee,
                    hash = tx.hash,
                    sigops = tx.sigops,
                    weight = tx.weight
                });
            }

            clone.transactions = ttx.ToArray();

            return clone;
        }

        public static SlimBlockHeader CreateSlimBlockHeader(this RPCBlockTemplate blockTemplate, bool isProofOfStake)
        {

            return new SlimBlockHeader
            {
                Bits = blockTemplate.ParseBits(isProofOfStake),
                // caution: use uint256.Parse when parsing the strings to account for the endianness
                HashPrevBlock = uint256.Parse(blockTemplate.previousblockhash).ToBytes(),
                Version = blockTemplate.version,
                Nonce = 0,
                Timestamp = 0,
                MerkleRoot = null,
                Data = null
            };
        }

        public static uint ParseBits(this RPCBlockTemplate blockTemplate, bool isProofOfStake)
        {
            return isProofOfStake
                ? Convert.ToUInt32(blockTemplate.posbits, 16)
                : Convert.ToUInt32(blockTemplate.bits, 16);
        }

        public static uint256 ParseStakeModifierV2(this RPCBlockTemplate blockTemplate)
        {
            if (blockTemplate != null && !string.IsNullOrWhiteSpace(blockTemplate.stakemodifierv2))
            {
                var value = uint256.Parse(blockTemplate.stakemodifierv2);
                if (value != uint256.Zero)
                    return value;
            }
            throw new InvalidOperationException($"{nameof(blockTemplate)} doesn't contain a valid {nameof(blockTemplate.stakemodifierv2)}.");
        }

        public static uint256 ParsePreviousBlockHash(this RPCBlockTemplate blockTemplate)
        {
            return uint256.Parse(blockTemplate.previousblockhash);
        }

        public static uint GetPreviousBlockTime(this RPCBlockTemplate blockTemplate)
        {
            return blockTemplate.previousblocktime;
        }
    }
}