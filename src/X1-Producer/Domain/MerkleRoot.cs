using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using X1.Producer.Domain.Tools;

namespace X1.Producer.Domain
{
    public static class MerkleRoot
    {
        static readonly SHA256 Sha256 = SHA256.Create();

        public static byte[] Build(IList<byte[]> merkleLeaves)
        {
            if (merkleLeaves == null || merkleLeaves.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(merkleLeaves));

            while (true)
            {
                if (merkleLeaves.Count == 1)
                {
                    return merkleLeaves[0];
                }

                if (merkleLeaves.Count % 2 > 0)
                {
                    merkleLeaves.Add(merkleLeaves[merkleLeaves.Count - 1]); // merkleLeaves[^1] last element
                }

                var merkleBranches = new List<byte[]>();

                for (int i = 0; i < merkleLeaves.Count; i += 2)
                {
                    var leafBytePair = ByteArrays.Concatenate(merkleLeaves[i], merkleLeaves[i + 1]);
                    var newMerkleBranch = DoubleSha256(leafBytePair);
                    merkleBranches.Add(newMerkleBranch);
                }

                merkleLeaves = merkleBranches;
            }
        }

        static byte[] DoubleSha256(byte[] data)
        {
            data = Sha256.ComputeHash(data);
            return Sha256.ComputeHash(data);
        }
    }
}