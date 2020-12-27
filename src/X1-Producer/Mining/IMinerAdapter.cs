namespace X1.Producer.Mining
{
    /// <summary>
    /// Wraps on or more miner instances.
    /// </summary>
    public interface IMinerAdapter
    {
        /// <summary>
        /// Start the miner instance at index instanceIndex
        /// </summary>
        /// <param name="headerBytes">The block header</param>
        /// <param name="targetBytes">The target</param>
        /// <param name="startNonce">The first nonce value to try</param>
        /// <param name="maxNonce">The last nonce value to try</param>
        /// <param name="elapsedMilliseconds">Execution time in milliseconds</param>
        /// <returns>The found nonce, or uint.MaxValue if none is found.</returns>
        uint Mine(byte[] headerBytes, byte[] targetBytes, uint startNonce, uint maxNonce, int instanceIndex, out long elapsedMilliseconds);

        /// <summary>
        /// Initializes the miner instance(s).
        /// </summary>
        void EnsureInitialized();

        /// <summary>
        /// The number of instances or threads.
        /// </summary>
        int InstancesCount { get; }

        /// <summary>
        /// Gets the description of the device.
        /// </summary>
        DeviceDescription GetDeviceDescription(int instanceIndex);

        /// <summary>
        /// The name of the device the instance is using.
        /// </summary>
        string GetDeviceName(int instanceIndex);
    }
}
