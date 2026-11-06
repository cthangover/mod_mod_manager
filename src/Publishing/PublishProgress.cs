namespace Mods.ModManager
{
    /// <summary>
    /// Upload state for the publish dialog: bytes the server has (or is being
    /// sent) out of the archive size, and the current retry streak (0 while the
    /// connection works).
    /// </summary>
    public readonly struct PublishProgress
    {
        public PublishProgress(long sent, long total, int retry)
        {
            Sent = sent;
            Total = total;
            Retry = retry;
        }

        /// <summary>Bytes stored by the server or in flight.</summary>
        public long Sent { get; }

        /// <summary>Archive size in bytes.</summary>
        public long Total { get; }

        /// <summary>Failed attempts in a row; 0 while the connection works.</summary>
        public int Retry { get; }
    }
}
