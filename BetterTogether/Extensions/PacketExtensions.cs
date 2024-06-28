using MemoryPack;

namespace BetterTogether.Extensions
{
    public static class PacketExtensions
    {
        /// <summary>
        /// Deserializes the data of the packet to the specified type
        /// </summary>
        /// <typeparam name="T">The type of the expected object</typeparam>
        /// <returns>The deserialized object or <c>null</c></returns>
        public static T? GetData<T>(this BetterTogether.Models.Packet packet)
        {
            if (packet.Data.Length == 0)
            {
                return default; // Return null if the data is empty (no data to deserialize)
            }

            return MemoryPackSerializer.Deserialize<T>(packet.Data);
        }

        /// <summary>
        /// Sets the data of the packet to the specified object
        /// </summary>
        /// <typeparam name="T"><c>MemoryPackable</c> object</typeparam>
        /// <param name="data">The object to serialize. The object must be Memorypackable.</param>
        public static void SetData<T>(this BetterTogether.Models.Packet packet, T data)
        {
            packet.Data = MemoryPackSerializer.Serialize(data);
        }

        /// <summary>
        /// Serializes the packet
        /// </summary>
        /// <returns>The serialized packet</returns>
        public static byte[] Pack(this BetterTogether.Models.Packet packet)
        {
            return MemoryPackSerializer.Serialize(packet);
        }
    }
}
