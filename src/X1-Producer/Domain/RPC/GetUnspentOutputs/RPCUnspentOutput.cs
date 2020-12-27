namespace X1.Producer.Domain.RPC.GetUnspentOutputs
{
    /// <summary>
    /// Unspent transaction output.
    /// listunspent ( minconf maxconf ["address",...] include_unsafe query_options )
    /// Returns array of unspent transaction outputs
    /// with between minconf and maxconf (inclusive) confirmations.
    /// Optionally filter to only include txouts paid to specified addresses.
    /// </summary>
    public class RPCUnspentOutput
    {
        /// <summary>
        /// The transaction id.
        /// </summary>
        public string txid;

        /// <summary>
        /// The vout value
        /// </summary>
        public int vout;

        /// <summary>
        /// The X1 address.
        /// </summary>
        public string address;

        /// <summary>
        /// The associated label, or "" for the default label.
        /// </summary>
        public string label;

        /// <summary>
        /// The script key.
        /// </summary>
        public string scriptPubKey;

        /// <summary>
        /// The transaction output amount in X1.
        /// </summary>
        public decimal amount;

        /// <summary>
        /// The number of confirmations.
        /// </summary>
        public int confirmations;

        /// <summary>
        /// Whether we have the private keys to spend this output.
        /// </summary>
        public bool spendable;

        /// <summary>
        /// Whether we know how to spend this output, ignoring the lack of keys.
        /// </summary>
        public bool solvable;

        /// <summary>
        /// (only when solvable) A descriptor for spending this output.
        /// </summary>
        public string desc;

        /// <summary>
        /// Whether this output is considered safe to spend. Unconfirmed transactions
        /// from outside keys and unconfirmed replacement transactions are considered unsafe
        /// and are not eligible for spending by fundrawtransaction and sendtoaddress.
        /// </summary>
        public bool safe;

        /// <summary>
        /// The RedeemScript if scriptPubKey is P2SH.
        /// </summary>
        public string redeemScript;

        /// <summary>
        /// WitnessScript if the scriptPubKey is P2WSH or P2SH-P2WSH.
        /// </summary>
        public string witnessScript;
    }
}
