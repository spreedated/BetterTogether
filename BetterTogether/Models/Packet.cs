using BetterTogether.Enumerations;
using MemoryPack;

namespace BetterTogether.Models
{
    [MemoryPackable]
    public partial record Packet
    {
        /// <summary>
        /// The type of the packet
        /// </summary>
        public PacketType Type { get; set; }

        /// <summary>
        /// The target of the packet. This can be an id or a name like "server"
        /// </summary>
        public string Target { get; set; } = "";

        /// <summary>
        /// The key is used differently depending on the packet type. For example, in a SetState packet, the key is the state name.
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// The data of the packet. This can be anything Memorypack can handle.
        /// </summary>
        public byte[] Data { get; set; } = [];

        #region Constructor
        /// <summary>
        /// Empty constructor
        /// </summary>
        public Packet()
        {
        }

        /// <summary>
        /// Constructor for a packet
        /// </summary>
        /// <param name="type">The packet type</param>
        /// <param name="target">The target of the packet</param>
        /// <param name="key">The key of the packet</param>
        /// <param name="data">The Memorypacked object to send</param>
        [MemoryPackConstructor]
        public Packet(PacketType type, string target, string key, byte[] data)
        {
            this.Type = type;
            this.Target = target;
            this.Key = key;
            this.Data = data;
        }
        #endregion

        /// <summary>
        /// Create a new packet with the specified data type. MemoryPack can't serialize <c>object</c> so generics are used.
        /// </summary>
        /// <param name="type">The packet type</param>
        /// <param name="target">The target of the packet</param>
        /// <param name="key">The key of the packet</param>
        /// <param name="data">The object to send. Must be Memorypackable</param>
        public static Packet New<T>(PacketType type, string target, string key, T data)
        {
            return new Packet(type, target, key, MemoryPackSerializer.Serialize(data));
        }
    }
}