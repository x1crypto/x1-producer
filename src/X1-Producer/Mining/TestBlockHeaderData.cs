using System;
using NBitcoin;
using X1.Producer.State;

namespace X1.Producer.Mining
{
    static class TestBlockHeaderData
    {
        public static Target GenesisMiningBits = new Target(new uint256("000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));

        public static Target GenesisBits = new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));

        public static uint256 GenesisHash = uint256.Parse("0000000e13c5bf36c155c7cb1681053d607c191fc44b863d0c5aef6d27b8eb8f");

        public static uint GenesisNonce = 15118976;

        public static Block CreateTestBlock()
        {
            uint genesisTime = Utils.DateTimeToUnixTime(new DateTime(2020, 1, 2, 23, 56, 00, DateTimeKind.Utc));
            int genesisVersion = 1;
            var genesisReward = Money.Zero;
            Block block = ComputeGenesisBlock(genesisTime, GenesisNonce, GenesisBits, genesisVersion, genesisReward);
            return block;
        }

        static Block ComputeGenesisBlock(uint genesisTime, uint genesisNonce, uint genesisBits, int genesisVersion, Money genesisReward)
        {
            Transaction txNew = C.Network.CreateTransaction();

            txNew.Version = 1;

            var scriptSig = C.Network.GetGenesis().Transactions[0].Inputs[0].ScriptSig;

            txNew.Inputs.Add(new TxIn
            {
                ScriptSig = scriptSig
            });

            txNew.Outputs.Add(new TxOut
            {
                Value = genesisReward,
            });
            Block genesis = C.Network.Consensus.ConsensusFactory.CreateBlock();
            genesis.Header.BlockTime = Utils.UnixTimeToDateTime(genesisTime);
            genesis.Header.Bits = genesisBits;
            genesis.Header.Nonce = genesisNonce;
            genesis.Header.Version = genesisVersion;
            genesis.Transactions.Add(txNew);
            genesis.Header.HashPrevBlock = uint256.Zero;
            genesis.UpdateMerkleRoot();

            if (genesis.GetHash() != GenesisHash ||
                genesis.Header.HashMerkleRoot != uint256.Parse("e3c549956232f0878414d765e83c3f9b1b084b0fa35643ddee62857220ea02b0"))
                throw new InvalidOperationException("Invalid network");
            return genesis;
        }
    }
}
