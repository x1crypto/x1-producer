using XDS.Producer.Domain.RPC.GetBlockTemplate;

namespace XDS.Producer.State
{
    public class BlockTemplateCache
    {
        readonly object lockObject = new object();

        RPCBlockTemplate currentBlockTemplate;

        long extraNonce;

        /// <summary>
        /// Set the new block template, generated from the response deserialization.
        /// </summary>
        public void SetBlockTemplateLocked(RPCBlockTemplate blockTemplate)
        {
            lock (lockObject)
            {
                if (blockTemplate == null || this.currentBlockTemplate == null || this.currentBlockTemplate.previousblockhash != blockTemplate.previousblockhash)
                    this.extraNonce = 0;

                this.currentBlockTemplate = blockTemplate;
            }
        }

        /// <summary>
        /// Get a clone of the block template, increasing the extra nonce every time.
        /// </summary>
        public RPCBlockTemplate GetClonedBlockTemplateLocked()
        {
            lock (lockObject)
            {
                if (this.currentBlockTemplate != null)
                {
                    var clone = currentBlockTemplate.Clone();
                    this.currentBlockTemplate.ExtraNonce = this.extraNonce;
                    this.extraNonce++;
                    return clone;
                }
            }

            return null;
        }
    }
}
