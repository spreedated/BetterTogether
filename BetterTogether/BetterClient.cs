using BetterTogether.Enumerations;
using BetterTogether.Extensions;
using BetterTogether.Models;
using LiteNetLib;
using LiteNetLib.Utils;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterTogether
{
    /// <summary>
    /// A BetterTogether client that connects to a BetterTogether server
    /// </summary>
    public class BetterClient : IDisposable
    {
        private readonly Dictionary<string, byte[]> _InitStates = [];
        private readonly ILogger? _Logger;
        private readonly Dictionary<string, Action<Packet>> _RegisteredEvents = [];
        private readonly Dictionary<string, ClientRpcAction> _RegisteredRPCs = [];
        private readonly ConcurrentDictionary<string, byte[]> _States = new();
        private DateTime _Ping;
        private List<string> _Players = [];
        private CancellationTokenSource? _PollToken = null;
        private bool disposedValue;

        #region Properties
        /// <summary>
        /// The id assigned to this client by the server
        /// </summary>
        public string Id { get; private set; } = "";

        public bool IsPolling { get; private set; }

        /// <summary>
        /// The underlying <c>LiteNetLib.EventBasedNetListener</c>
        /// </summary>
        public EventBasedNetListener Listener { get; private set; } = new EventBasedNetListener();

        /// <summary>
        /// The underlying <c>LiteNetLib.NetManager</c>
        /// </summary>
        public NetManager? NetManager { get; private set; } = null;

        /// <summary>
        /// Returns a list of all connected players
        /// </summary>
        public List<string> Players => new(this._Players);

        /// <summary>
        /// The delay between polling events in milliseconds. Default is 15ms
        /// </summary>
        public int PollInterval { get; private set; } = 15;
        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new BetterClient
        /// </summary>
        public BetterClient(ILogger? logger = null)
        {
            this._Logger = logger;
            this.Listener.NetworkReceiveEvent += this.Listener_NetworkReceiveEvent;
            this.Listener.PeerDisconnectedEvent += this.Listener_PeerDisconnectedEvent;
        }
        #endregion

        #region Private Methods
        private void AddStates(IDictionary<string, byte[]> states, bool clear = false)
        {
            if (clear)
            {
                this._InitStates.Clear();
            }

            foreach (KeyValuePair<string, byte[]> state in states)
            {
                this._InitStates[state.Key] = state.Value;
            }
        }

        private void ClearAllGlobalStates(IEnumerable<string> except)
        {
            List<KeyValuePair<string, byte[]>> globalStates = this._States.Where(x => !PreCompiledRegex.GuidRegex().IsMatch(x.Key) && !except.Contains(x.Key)).ToList();
            foreach (var state in globalStates)
            {
                this._States.TryRemove(state.Key, out _);
            }
        }

        private void ClearAllPlayerStates(IEnumerable<string> except)
        {
            var playerStates = this._States.Where(x => PreCompiledRegex.GuidRegex().IsMatch(x.Key) && !except.Contains(x.Key[..36])).ToList();
            foreach (var state in playerStates)
            {
                this._States.TryRemove(state.Key, out _);
            }
        }

        private void ClearSpecificPlayerStates(string player, IEnumerable<string> except)
        {
            foreach (var key in except)
            {
                this._States.TryRemove(player + key, out _);
            }
        }

        private void DeletePlayerState(string player, string key)
        {
            this._States.TryRemove(player + key, out _);
        }

        private void DeleteState(string key)
        {
            this._States.TryRemove(key, out _);
        }

        private void HandleRPC(string method, string player, byte[] args)
        {
            if (this._RegisteredRPCs.TryGetValue(method, out ClientRpcAction? value))
            {
                value(player, args);
            }
        }
        private void Listener_NetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            if (reader.AvailableBytes > 0)
            {
                byte[] bytes = reader.GetRemainingBytes();
                Packet? packet = MemoryPackSerializer.Deserialize<Packet>(bytes);

                if (packet == null)
                {
                    return;
                }

                switch (packet.Type)
                {
                    case PacketType.Ping:
                        if (packet.Target == "server") _Ping = DateTime.Now;
                        else if (packet.Target == this.Id)
                        {
                            _Ping = DateTime.Now;
                        }
                        else if (packet.Target != "server")
                        {
                            Packet pong = new()
                            {
                                Type = PacketType.Ping,
                                Target = packet.Target,
                                Key = "pong"
                            };
                            peer.Send(pong.Pack(), deliveryMethod);
                        }
                        break;
                    case PacketType.SetState:
                        if (packet.Target == "FORBIDEN")
                        {
                            this._States[packet.Key] = packet.Data;
                        }
                        this._States[packet.Key] = packet.Data;
                        if (this._RegisteredEvents.TryGetValue(packet.Key, out Action<Packet>? value))
                        {
                            value(packet);
                        }
                        break;
                    case PacketType.Init:
                        var states = packet.GetData<ConcurrentDictionary<string, byte[]>>();
                        if (states != null) this.AddStates(states, true);
                        break;
                    case PacketType.RPC:
                        this.HandleRPC(packet.Key, packet.Target, packet.Data);
                        break;
                    case PacketType.DeleteState:
                        List<string>? except = packet.GetData<List<string>>();
                        if (packet.Target.Length == 36 && this.Players.Contains(packet.Target))
                        {
                            if (packet.Key != "")
                            {
                                this.DeletePlayerState(packet.Target, packet.Key);
                            }
                            else if (except != null)
                            {
                                this.ClearSpecificPlayerStates(packet.Target, except);
                            }
                        }
                        else if (packet.Target == "players")
                        {
                            if (except != null)
                            {
                                this.ClearAllPlayerStates(except);
                            }
                        }
                        else if (packet.Target == "global")
                        {
                            if (packet.Key != "")
                            {
                                this.DeleteState(packet.Key);
                            }
                            if (except != null)
                            {
                                this.ClearAllGlobalStates(except);
                            }
                        }
                        break;
                    case PacketType.SelfConnected:
                        List<string>? list = packet.GetData<List<string>>();
                        if (list != null && list.Count > 0)
                        {
                            this.Id = list[0];
                            this._Players = list;
                            list.Remove(this.Id);
                            Connected?.Invoke(this.Id, list);
                        }
                        break;
                    case PacketType.PeerConnected:
                        string connectedId = Encoding.UTF8.GetString(packet.Data);
                        this._Players.Add(connectedId);
                        PlayerConnected?.Invoke(connectedId);
                        break;
                    case PacketType.PeerDisconnected:
                        string disconnectedId = Encoding.UTF8.GetString(packet.Data);
                        this._Players.Remove(disconnectedId);
                        PlayerDisconnected?.Invoke(disconnectedId);
                        break;
                    case PacketType.Kick:
                        Kicked?.Invoke(Encoding.UTF8.GetString(packet.Data));
                        break;
                    case PacketType.Ban:
                        Banned?.Invoke(Encoding.UTF8.GetString(packet.Data));
                        break;
                    default:
                        break;
                }
            }
        }

        private void Listener_PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            this.Disconnected?.Invoke(disconnectInfo);
        }
        private async Task PollEvents()
        {
            while (this.NetManager != null)
            {
                if (this._PollToken!.IsCancellationRequested)
                {
                    this.IsPolling = false;
                    return;
                }

                this.IsPolling = true;
                this.NetManager.PollEvents();

                await Task.Delay(this.PollInterval);
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// A delegate for RPC actions on the client
        /// </summary>
        /// <param name="player">The id of the player that invoked the RPC</param>
        /// <param name="args">The MemoryPacked arguments</param>
        public delegate void ClientRpcAction(string player, byte[] args);

        /// <summary>
        /// Fired when a player is banned from the server. The string is the reason of the ban
        /// </summary>
        public event Action<string>? Banned;

        /// <summary>
        /// Fired when the client is connected to the server. The string is the id assigned to this client by the server. You can also use <c>Client.Id</c> as it is assigned before this is called. The List is the list of all connected players exluding this player
        /// </summary>
        public event Action<string, List<string>>? Connected;

        /// <summary>
        /// Fired when the client is disconnected from the server
        /// </summary>
        public event Action<DisconnectInfo>? Disconnected;

        /// <summary>
        /// Fired when a player is kicked from the server. The string is the reason of the kick
        /// </summary>
        public event Action<string>? Kicked;

        /// <summary>
        /// Fired when a player is connected to the server. The string is the id of the player
        /// </summary>
        public event Action<string>? PlayerConnected;

        /// <summary>
        /// Fired when a player is disconnected from the server. The string is the id of the player
        /// </summary>
        public event Action<string>? PlayerDisconnected;

        /// <summary>
        /// Connects the client to the target server
        /// </summary>
        /// <param name="host">The address of the server</param>
        /// <param name="port">The port of the server</param>
        /// <returns>True if the connection was successful</returns>
        public bool Connect(string host, int port = 9050)
        {
            this.NetManager = new(this.Listener);
            try
            {
                this.NetManager.Start();
                ConnectionData connectionData = new(Constants.DEFAULT_KEY, this._InitStates);
                NetDataWriter writer = new();
                byte[] data = MemoryPackSerializer.Serialize(connectionData);
                this._Logger?.LogTrace("Data length \"{Length}\"", data.Length);
                writer.Put(data);
                IPEndPoint endPoint = new(IPAddress.Parse(host), port);
                if (this.NetManager.Connect(endPoint, writer) != null)
                {
                    this._PollToken = new();
                    Task.Run(this.PollEvents);

                    return this.IsPolling;
                }
                else return false;
            }
            catch
            {
                this.NetManager.Stop();
                this.NetManager = null;
                return false;
            }
        }

        /// <summary>
        /// Disconnects the client from the server
        /// </summary>
        /// <returns>This client</returns>
        public BetterClient Disconnect()
        {
            this._PollToken?.Cancel();
            if (this.NetManager == null) return this;
            this.Id = "";
            this._Players.Clear();
            this._States.Clear();
            this.NetManager.DisconnectPeer(this.NetManager.FirstPeer);
            this.NetManager?.Stop();
            this.NetManager = null;
            return this;
        }

        /// <summary>
        /// Gets the latest state of a player specific key available on this client
        /// </summary>
        /// <typeparam name="T">The expected type of the object. Must be MemoryPackable</typeparam>
        /// <param name="playerId">The id of the player</param>
        /// <param name="key">The name of the state object</param>
        /// <returns>The deserialized object or the default value of the expected type</returns>
        public T? GetPlayerState<T>(string playerId, string key)
        {
            string finalKey = playerId + key;
            if (this._States.TryGetValue(finalKey, out byte[]? value))
            {
                return MemoryPackSerializer.Deserialize<T>(value);
            }

            return default;
        }

        /// <summary>
        /// Gets the latest state of a key available on this client
        /// </summary>
        /// <typeparam name="T">The expected type of the object. Must be MemoryPackable</typeparam>
        /// <param name="key">The name of the state object</param>
        /// <returns>The deserialized object or the default value of the expected type</returns>
        public T? GetState<T>(string key)
        {
            if (this._States.TryGetValue(key, out byte[]? value))
            {
                return MemoryPackSerializer.Deserialize<T>(value);
            }

            return default;
        }

        /// <summary>
        /// Removes an action from the registered events
        /// </summary>
        /// <param name="key">The key of the state</param>
        /// <returns>This client</returns>
        public BetterClient Off(string key)
        {
            this._RegisteredEvents.Remove(key);
            return this;
        }

        /// <summary>
        /// Registers an action to be invoked when a <c>PacketType.SetState</c> packet with a specific key is received
        /// </summary>
        /// <param name="key">The key of the state</param>
        /// <param name="action">The action to be invoked</param>
        /// <returns>This client</returns>
        public BetterClient On(string key, Action<Packet> action)
        {
            this._RegisteredEvents[key] = action;
            return this;
        }

        /// <summary>
        /// Fluent version of <c>Banned</c>
        /// </summary>
        /// <param name="action">Action to invoke</param>
        /// <returns>This client</returns>
        public BetterClient OnBanned(Action<string> action)
        {
            Banned += action;
            return this;
        }

        // Events
        /// <summary>
        /// Fluent version of <c>Connected</c>
        /// </summary>
        /// <param name="action">Action to invoke</param>
        /// <returns>This client</returns>
        public BetterClient OnConnected(Action<string, List<string>> action)
        {
            Connected += action;
            return this;
        }

        /// <summary>
        /// Fluent version of <c>Disconnected</c>
        /// </summary>
        /// <param name="action">Action to invoke</param>
        /// <returns>This client</returns>
        public BetterClient OnDisconnected(Action<DisconnectInfo> action)
        {
            Disconnected += action;
            return this;
        }

        /// <summary>
        /// Fluent version of <c>Kicked</c>
        /// </summary>
        /// <param name="action">Action to invoke</param>
        /// <returns>This client</returns>
        public BetterClient OnKicked(Action<string> action)
        {
            Kicked += action;
            return this;
        }

        /// <summary>
        /// Fluent version of <c>PlayerConnected</c>
        /// </summary>
        /// <param name="action">Action to invoke</param>
        /// <returns>This client</returns>
        public BetterClient OnPlayerConnected(Action<string> action)
        {
            PlayerConnected += action;
            return this;
        }

        /// <summary>
        /// Fluent version of <c>PlayerDisconnected</c>
        /// </summary>
        /// <param name="action"></param>
        /// <returns>This client</returns>
        public BetterClient OnPlayerDisconnected(Action<string> action)
        {
            PlayerDisconnected += action;
            return this;
        }

        /// <summary>
        /// Sends a ping to a player and returns the delay. Only call once at a time
        /// </summary>
        /// <param name="playerId">The id of the target player</param>
        /// <param name="timeout">The maximum time to wait</param>
        /// <param name="method">The delivery method of LiteNetLib</param>
        /// <returns>The delay as a <c>TimeSpan</c></returns>
        public async Task<TimeSpan> PingPlayer(string playerId, int timeout = 2000, DeliveryMethod method = DeliveryMethod.Unreliable)
        {
            if (this.NetManager == null)
            {
                return TimeSpan.Zero;
            }

            Packet packet = new()
            {
                Target = playerId,
                Type = PacketType.Ping
            };

            this.NetManager.FirstPeer.Send(packet.Pack(), method);
            DateTime now = DateTime.Now;
            TimeSpan delay = now - await Task.Run(() =>
            {
                int i = 0;
                while (_Ping == default && i < timeout)
                {
                    Thread.Sleep(15);
                    i += 15;
                }
                if (_Ping == default) return now;
                else return _Ping;
            });
            _Ping = default;

            return delay;
        }

        /// <summary>
        /// Pings the server and returns the delay. Only call once at a time
        /// </summary>
        /// <param name="timeout">The maximum time to wait for a response</param>
        /// <param name="method">The delivery method of LiteNetLib</param>
        /// <returns>The delay as a <c>TimeSpan</c></returns>
        public async Task<TimeSpan> PingServer(int timeout = 2000, DeliveryMethod method = DeliveryMethod.Unreliable)
        {
            if (this.NetManager == null)
            {
                return TimeSpan.Zero;
            }

            Packet packet = new()
            {
                Target = "server",
                Type = PacketType.Ping
            };

            this.NetManager.FirstPeer.Send(packet.Pack(), method);
            DateTime now = DateTime.Now;
            TimeSpan delay = now - await Task.Run(() =>
            {
                int i = 0;
                while (_Ping == default && i < timeout)
                {
                    Thread.Sleep(15);
                    i += 15;
                }
                if (_Ping == default) return now;
                else return _Ping;
            });
            _Ping = default;

            return delay;
        }

        /// <summary>
        /// Registers a Remote Procedure Call with a method name and an action to invoke
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="action">The method</param>
        /// <returns>This client</returns>
        public BetterClient RegisterRPC(string method, ClientRpcAction action)
        {
            this._RegisteredRPCs[method] = action;
            return this;
        }

        /// <summary>
        /// Sends a Remote Procedure Call to all players including the current player
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The MemoryPacked arguments for the method</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcAll(string method, byte[] args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            if (this.NetManager == null) return this;
            Packet packet = new()
            {
                Type = PacketType.RPC,
                Target = "all",
                Key = method,
                Data = args
            };
            this.NetManager.FirstPeer.Send(packet.Pack(), delMethod);
            return this;
        }

        /// <summary>
        /// Sends a Remote Procedure Call to all players including the current player
        /// </summary>
        /// <typeparam name="T">The type of the arguments. Must be MemoryPackable</typeparam>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The arguments for the method. Must be MemoryPackable</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcAll<T>(string method, T args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            byte[] bytes = MemoryPackSerializer.Serialize(args);
            return this.RpcAll(method, bytes, delMethod);
        }

        /// <summary>
        /// Sends a Remote Procedure Call to all players except the current player
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The MemoryPacked arguments for the method</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcOthers(string method, byte[] args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            if (this.NetManager == null)
            {
                return this;
            }

            Packet packet = new()
            {
                Type = PacketType.RPC,
                Target = "others",
                Key = method,
                Data = args
            };
            this.NetManager.FirstPeer.Send(packet.Pack(), delMethod);

            return this;
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the server
        /// </summary>
        /// <typeparam name="T">The type of the arguments. Must be MemoryPackable</typeparam>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The arguments for the method. Must be MemoryPackable</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcOthers<T>(string method, T args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            byte[] bytes = MemoryPackSerializer.Serialize(args);
            return this.RpcOthers(method, bytes, delMethod);
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the target player
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="target">The id of the target player</param>
        /// <param name="args">The MemoryPacked arguments for the method</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcPlayer(string method, string target, byte[] args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            if (this.NetManager == null) return this;
            Packet packet = new()
            {
                Type = PacketType.RPC,
                Target = target,
                Key = method,
                Data = args
            };
            this.NetManager.FirstPeer.Send(packet.Pack(), delMethod);
            return this;
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the target player
        /// </summary>
        /// <typeparam name="T">The type of the arguments. Must be MemoryPackable</typeparam>
        /// <param name="method">The name of the method</param>
        /// <param name="target">The id of the target player</param>
        /// <param name="args">The arguments for the method. Must be MemoryPackable</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcPlayer<T>(string method, string target, T args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            byte[] bytes = MemoryPackSerializer.Serialize(args);
            return this.RpcPlayer(method, target, bytes, delMethod);
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the server then back to this client
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The MemoryPacked arguments for the method</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcSelf(string method, byte[] args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            if (this.NetManager == null) return this;
            Packet packet = new()
            {
                Type = PacketType.RPC,
                Target = "self",
                Key = method,
                Data = args
            };
            this.NetManager.FirstPeer.Send(packet.Pack(), delMethod);
            return this;
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the server then back to this client
        /// </summary>
        /// <typeparam name="T">The type of the arguments. Must be MemoryPackable</typeparam>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The arguments for the method. Must be MemoryPackable</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcSelf<T>(string method, T args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            byte[] bytes = MemoryPackSerializer.Serialize(args);
            return this.RpcSelf(method, bytes, delMethod);
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the server
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The MemoryPacked arguments for the method</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcServer(string method, byte[] args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            if (this.NetManager == null) return this;
            Packet packet = new()
            {
                Type = PacketType.RPC,
                Target = "server",
                Key = method,
                Data = args
            };
            this.NetManager.FirstPeer.Send(packet.Pack(), delMethod);
            return this;
        }

        /// <summary>
        /// Sends a Remote Procedure Call to the server
        /// </summary>
        /// <typeparam name="T">The type of the arguments. Must be MemoryPackable</typeparam>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The arguments for the method. Must be MemoryPackable</param>
        /// <param name="delMethod">The delivery method of LiteNetLib</param>
        /// <returns>This client</returns>
        public BetterClient RpcServer<T>(string method, T args, DeliveryMethod delMethod = DeliveryMethod.ReliableOrdered)
        {
            byte[] bytes = MemoryPackSerializer.Serialize(args);
            return this.RpcServer(method, bytes, delMethod);
        }

        /// <summary>
        /// Sends a state object to the server. This state object is owned by the player and only this client or the server can modify it
        /// </summary>
        /// <typeparam name="T">The type of the object. Must be MemoryPackable</typeparam>
        /// <param name="key">The name of the state to set</param>
        /// <param name="data">The object to send. Must be MemoryPackable</param>
        /// <param name="method"></param>
        public void SetPlayerState<T>(string key, T data, DeliveryMethod method = DeliveryMethod.ReliableUnordered)
        {
            this.SetPlayerState(key, MemoryPackSerializer.Serialize(data), method);
        }

        /// <summary>
        /// Sends a state object to the server. This state object is owned by the player and only this client or the server can modify it
        /// </summary>
        /// <param name="key">The name of the state to set</param>
        /// <param name="data">The MemoryPacked object</param>
        /// <param name="method">The delivery method of LiteNetLib</param>
        public void SetPlayerState(string key, byte[] data, DeliveryMethod method = DeliveryMethod.ReliableUnordered)
        {
            if (this.NetManager == null)
            {
                return;
            }

            this._States[this.Id + key] = data;
            Packet packet = new()
            {
                Type = PacketType.SetState,
                Target = this.Id,
                Key = this.Id + key,
                Data = data
            };

            this.NetManager.FirstPeer.Send(packet.Pack(), method);
        }

        /// <summary>
        /// Sends a state object to the server
        /// </summary>
        /// <typeparam name="T">The type of the object. Must be MemoryPackable</typeparam>
        /// <param name="key">The name of the state to set</param>
        /// <param name="data">The object to send. Must be MemoryPackable</param>
        /// <param name="method">The delivery method of LiteNetLib</param>
        public void SetState<T>(string key, T data, DeliveryMethod method = DeliveryMethod.ReliableUnordered)
        {
            this.SetState(key, MemoryPackSerializer.Serialize(data), method);
        }

        /// <summary>
        /// Sends a state object to the server
        /// </summary>
        /// <param name="key">The name of the state to set</param>
        /// <param name="data">The MemoryPacked object</param>
        /// <param name="method">The delivery method of LiteNetLib</param>
        public void SetState(string key, byte[] data, DeliveryMethod method = DeliveryMethod.ReliableUnordered)
        {
            if (this.NetManager == null) return;
            if (key.Length >= 36 && PreCompiledRegex.GuidRegex().IsMatch(key)) return;
            this._States[key] = data;
            Packet packet = new()
            {
                Type = PacketType.SetState,
                Key = key,
                Data = data
            };
            this.NetManager.FirstPeer.Send(packet.Pack(), method);
        }

        /// <summary>
        /// Sets the initial states of the client
        /// </summary>
        /// <param name="states"></param>
        /// <returns>This client</returns>
        public BetterClient WithInitStates(IDictionary<string, byte[]> states)
        {
            this.AddStates(states);
            return this;
        }

        /// <summary>
        /// Sets the interval between polling events. Default is 15ms
        /// </summary>
        /// <param name="interval"></param>
        /// <returns>This client</returns>
        public BetterClient WithPollInterval(int interval)
        {
            this.PollInterval = interval;
            return this;
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.Disconnect();
                    this._PollToken?.Dispose();
                }

                disposedValue = true;
            }
        }
        #endregion
    }
}