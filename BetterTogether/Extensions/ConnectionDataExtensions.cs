using BetterTogether.Models;
using MemoryPack;

namespace BetterTogether.Extensions
{
    public static class ConnectionDataExtensions
    {
        /// <summary>
        /// Sets the specified state
        /// </summary>
        /// <typeparam name="T">The type of the state. Must be MemoryPackable</typeparam>
        /// <param name="key">The key of the state</param>
        /// <param name="data">The object</param>
        /// <returns>This object</returns>
        public static void SetState<T>(ConnectionData connectionData, string key, T data)
        {
            connectionData.InitStates[key] = MemoryPackSerializer.Serialize(data);
        }

        /// <summary>
        /// Deletes the specified state
        /// </summary>
        /// <param name="key">The key of the state</param>
        /// <returns>This object</returns>
        public static void DeleteState(ConnectionData connectionData, string key)
        {
            connectionData.InitStates.Remove(key);
        }
    }
}
