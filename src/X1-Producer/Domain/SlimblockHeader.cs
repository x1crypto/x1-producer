using System;
using System.Linq;
using NBitcoin;
using NBitcoin.Crypto;
using X1.Producer.Domain.Tools;

namespace X1.Producer.Domain
{
    public sealed class SlimBlockHeader
    {
        /// <summary>
        /// The 80-bytes serialized block header for hashing.
        /// </summary>
        public byte[] Data;

        /// <summary>
        /// 4 bytes, int32_t (signed)
        /// </summary>
        public int Version;

        public byte[] HashPrevBlock;

        public byte[] MerkleRoot;

        /// <summary>
        /// 4 bytes, uint32_t (unsigned)
        /// </summary>
        public uint Timestamp;

        /// <summary>
        /// 4 bytes, uint32_t (unsigned)
        /// </summary>
        public uint Bits;

        /// <summary>
        /// 4 bytes, uint32_t (unsigned)
        /// </summary>
        public uint Nonce;
    }

    public static class SlimBlockHeaderExtensions
    {
        public static SlimBlockHeader Clone(this SlimBlockHeader slimBlockHeader)
        {
            var clone = new SlimBlockHeader
            {
                Version = slimBlockHeader.Version,
                Timestamp = slimBlockHeader.Timestamp,
                Bits = slimBlockHeader.Bits,
                Nonce = slimBlockHeader.Nonce,
            };

            if (slimBlockHeader.Data != null)
            {
                clone.Data = new byte[80];
                Buffer.BlockCopy(slimBlockHeader.Data, 0, clone.Data, 0, 80);
            }

            if (slimBlockHeader.HashPrevBlock != null)
            {
                clone.HashPrevBlock = new byte[32];
                Buffer.BlockCopy(slimBlockHeader.HashPrevBlock, 0, clone.HashPrevBlock, 0, 32);
            }

            if (slimBlockHeader.MerkleRoot != null)
            {
                clone.MerkleRoot = new byte[32];
                Buffer.BlockCopy(slimBlockHeader.MerkleRoot, 0, clone.MerkleRoot, 0, 32);
            }

            return clone;
        }

        public static byte[] SerializeTo80Bytes(this SlimBlockHeader slimBlockHeader)
        {
            var versionBytes = BitConverter.GetBytes(slimBlockHeader.Version);
            var timeBytes = BitConverter.GetBytes(slimBlockHeader.Timestamp);
            var bitsBytes = BitConverter.GetBytes(slimBlockHeader.Bits);
            var nonceBytes = BitConverter.GetBytes(slimBlockHeader.Nonce);

            return ByteArrays.Concatenate(versionBytes, slimBlockHeader.HashPrevBlock, slimBlockHeader.MerkleRoot, timeBytes, bitsBytes, nonceBytes);
        }

        public static uint256 GetDoubleSHA256(this SlimBlockHeader slimBlockHeader)
        {
            return Hashes.DoubleSHA256(slimBlockHeader.Data);
        }

        public static void IncrementNonce(this SlimBlockHeader slimBlockHeader)
        {
            slimBlockHeader.Nonce++;
            var nonceBytes = BitConverter.GetBytes(slimBlockHeader.Nonce);
            Buffer.BlockCopy(nonceBytes, 0, slimBlockHeader.Data, 76, 4);
        }

        public static SlimBlockHeader ToSlimBlockHeader(this Block block)
        {
            var slimBlockHeader = new SlimBlockHeader
            {
                Version = block.Header.Version,
                HashPrevBlock = block.Header.HashPrevBlock.ToBytes(),
                Bits = block.Header.Bits,
                MerkleRoot = MerkleRoot.Build(block.Transactions.Select(x => x.GetHash().ToBytes()).ToList()),
                Timestamp = (uint)block.Header.BlockTime.ToUnixTimeSeconds(),
                Nonce = 0,
            };

            return slimBlockHeader;
        }

        public static unsafe void IncrementNonceUnsafe(this SlimBlockHeader slimBlockHeader)
        {
            slimBlockHeader.Nonce++;

            fixed (byte* noncePosInData = &slimBlockHeader.Data[76])
            {
                uint* noncePointer = (uint*)noncePosInData;
                *noncePointer = *noncePointer + (uint)1;
            }
        }

        public static unsafe uint GetFinalNonce(this SlimBlockHeader slimBlockHeader)
        {
            fixed (byte* noncePosInData = &slimBlockHeader.Data[76])
            {
                uint* noncePointer = (uint*)noncePosInData;
                return *noncePointer;
            }
        }

        public static unsafe void SetFinalNonce(this SlimBlockHeader slimBlockHeader, uint solutionNonce)
        {
            fixed (byte* noncePosInData = &slimBlockHeader.Data[76])
            {
                uint* noncePointer = (uint*)noncePosInData;
                *noncePointer = solutionNonce;
            }
        }

        public static unsafe uint ExtractNonceFromBytesUnsafe(this SlimBlockHeader slimBlockHeader)
        {
            fixed (byte* noncePosInData = &slimBlockHeader.Data[76])
            {
                uint* noncePointer = (uint*)noncePosInData;
                return *noncePointer;
            }
        }
    }
}