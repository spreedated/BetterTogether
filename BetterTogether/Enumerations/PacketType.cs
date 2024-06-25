namespace BetterTogether.Enumerations
{
    /// <summary>
    /// Various types of packets
    /// </summary>
    public enum PacketType
    {
        /// <summary>
        /// Default value. Doesn't mean anything.
        /// </summary>
        None,
        /// <summary>
        /// Used to set the state of a peer
        /// </summary>
        SetState,
        /// <summary>
        /// Used to delete the state of a peer
        /// </summary>
        DeleteState,
        /// <summary>
        /// Sent to the peer with the current state and other data
        /// </summary>
        Init,
        /// <summary>
        /// Used for all ping related stuff
        /// </summary>
        Ping,
        /// <summary>
        /// Used for RPC calls
        /// </summary>
        RPC,
        /// <summary>
        /// Sent to the connected peer when the connection is established
        /// </summary>
        SelfConnected,
        /// <summary>
        /// Sent to all peers when a peer connects
        /// </summary>
        PeerConnected,
        /// <summary>
        /// Sent to all peers when a peer disconnects
        /// </summary>
        PeerDisconnected,
        /// <summary>
        /// Sent to kicked peers
        /// </summary>
        Kick,
        /// <summary>
        /// Sent to banned peers
        /// </summary>
        Ban
    }
}
