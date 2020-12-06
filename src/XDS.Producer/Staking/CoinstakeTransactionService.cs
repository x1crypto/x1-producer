using System;
using System.Diagnostics;
using NBitcoin;
using XDS.Producer.Domain.Addresses;
using XDS.Producer.State;

namespace XDS.Producer.Staking
{
    static class CoinstakeTransactionService
    {
        public static Transaction CreateAndSignCoinstakeTransaction(SegWitCoin kernelCoin, long totalReward, uint currentBlockTime, string passphrase, out Key privateKey)
        {
            var tx = CreateCoinstakeTransaction(kernelCoin, totalReward, currentBlockTime, passphrase, out privateKey);

            SigningService.SignInputs(tx, new[] { privateKey }, new[] { kernelCoin });

            CheckTransaction(tx, kernelCoin);

            return tx;
        }

        static void CheckTransaction(Transaction tx, SegWitCoin kernelCoin)
        {
            var txData = new PrecomputedTransactionData(tx);

            for (int inputIndex = 0; inputIndex < tx.Inputs.Count; inputIndex++)
            {
                TxIn input = tx.Inputs[inputIndex];

                TxOut spentOut = new TxOut(kernelCoin.UtxoValue, kernelCoin.SegWitAddress.GetScriptPubKey());

                var checker = new TransactionChecker(tx, inputIndex, spentOut, txData);
                var ctx = new ScriptEvaluationContext
                {
                    ScriptVerify = ScriptVerify.Mandatory | ScriptVerify.DerSig | ScriptVerify.CheckLockTimeVerify |
                                   ScriptVerify.Witness /* todo | ScriptVerify.CheckColdStakeVerify*/
                };
                bool verifyScriptResult = ctx.VerifyScript(input.ScriptSig, spentOut.ScriptPubKey, checker);

                if (verifyScriptResult == false)
                {
                    throw new InvalidOperationException(
                        $"Verify script for transaction '{tx.GetHash()}' input {inputIndex} failed, ScriptSig = '{input.ScriptSig}', ScriptPubKey = '{spentOut.ScriptPubKey}', script evaluation error = '{ctx.Error}'");
                }
            }
        }

        public static Transaction CreateCoinstakeTransaction(SegWitCoin kernelCoin, long totalReward, uint currentBlockTime, string passphrase, out Key privateKey)
        {
            Transaction tx = C.Network.CreateTransaction();

            if (kernelCoin.SegWitAddress is ColdStakingAddress coldStakingAddress &&
                coldStakingAddress.AddressType == AddressType.ColdStakingHot)
                privateKey = new Key(coldStakingAddress.StakingKey);
            else
            {
                // the purple staking way
                if (passphrase == null && kernelCoin.SegWitAddress is PubKeyHashAddress pkha && pkha.KeyMaterial.PlaintextBytes != null)
                    privateKey = new Key(pkha.KeyMaterial.PlaintextBytes);
                else
                    // the x1wallet way
                    privateKey = kernelCoin.GetPrivateKey(passphrase);
            }


            tx.Inputs.Add(new TxIn(new OutPoint(kernelCoin.UtxoTxHash, kernelCoin.UtxoTxN)));

            tx.Outputs.Add(new TxOut(0, Script.Empty));
            tx.Outputs.Add(new TxOut(0, new Script(OpcodeType.OP_RETURN, Op.GetPushOp(privateKey.PubKey.Compress().ToBytes()))));
            tx.Outputs.Add(new TxOut(totalReward + kernelCoin.UtxoValue, kernelCoin.SegWitAddress.GetScriptPubKey()));
            Debug.Assert(kernelCoin.SegWitAddress.GetScriptPubKey() == privateKey.PubKey.Compress().WitHash.ScriptPubKey);
            return tx;
        }
    }
}
