using MemoryPack;
using System.Collections.Generic;

namespace BetterTogether.Models
{
    /// <summary>
    /// THis struct is sent to the server to establish a connection along with initial states
    /// </summary>
    [MemoryPackable]
    public partial record ConnectionData
    {
        /// <summary>
        /// The key of the connection
        /// </summary>
        public string Key { get; set; } = "BetterTogether";

        /// <summary>
        /// The initial states
        /// </summary>
        public Dictionary<string, byte[]> InitStates { get; set; } = [];

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        public ConnectionData()
        {
        }

        /// <summary>
        /// Constructor with key
        /// </summary>
        /// <param name="key">The key</param>
        public ConnectionData(string key)
        {
            this.Key = key;
        }

        /// <summary>
        /// Constructor with key and initial states
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="initStates">Initial states</param>
        [MemoryPackConstructor]
        public ConnectionData(string key, Dictionary<string, byte[]> initStates)
        {
            this.Key = key;
            this.InitStates = initStates;
        }
        #endregion
    }
}
