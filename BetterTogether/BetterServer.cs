﻿using BetterTogether.Enumerations;
using BetterTogether.Extensions;
using BetterTogether.Models;
using LiteNetLib;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterTogether
{
    /// <summary>
    /// The BetterTogether server. Create one with a max player count then use the Start method to start the server on the specified port. Set the <c>DataReceived</c> <c>Func<![CDATA[<]]>NetPeer, Packet, Packet<![CDATA[>]]></c> for your data validation and handling.
    /// </summary>
    public class BetterServer : IDisposable
    {
        private readonly ConcurrentDictionary<string, bool> _Admins = new();
        private readonly List<string> _Banned = [];
        private readonly ILogger? _Logger;
        private readonly ConcurrentDictionary<string, NetPeer> _Players = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, byte[]>> _PlayerStatesToSet = new();
        private readonly Dictionary<string, ServerRpcAction> _RegisteredRPCs = [];
        private readonly ConcurrentDictionary<string, byte[]> _States = new();
        private CancellationTokenSource? _PollToken;
        private bool disposedValue;

        #region Properties
        /// <summary>
        /// Whether this server allows admin users
        /// </summary>
        public bool AllowAdminUsers { get; private set; }

        public bool IsPolling { get; private set; }

        /// <summary>
        /// The underlying <c>LiteNetLib.EventBasedNetListener</c>
        /// </summary>
        public EventBasedNetListener Listener { get; private set; } = new();

        /// <summary>
        /// The max amount of players
        /// </summary>
        public int MaxPlayers { get; private set; } = 10;

        /// <summary>
        /// The underlying <c>LiteNetLib.NetManager</c>
        /// </summary>
        public NetManager? NetManager { get; private set; }

        /// <summary>
        /// Returns a read-only dictionary of the players on the server
        /// </summary>
        public ReadOnlyDictionary<string, NetPeer> Players => new(this._Players);

        /// <summary>
        /// The delay between polling events in milliseconds. Default is 15ms
        /// </summary>
        public int PollInterval { get; private set; } = 15;
        /// <summary>
        /// The reserved states for the server. Only the server (and admins if setup correctly) can modify these states
        /// </summary>
        public List<string> ReservedStates { get; private set; } = [];

        /// <summary>
        /// Returns a read-only dictionary of the states on the server
        /// </summary>
        public ReadOnlyDictionary<string, byte[]> States => new(this._States);
        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new server
        /// </summary>
        public BetterServer(ILogger? logger = null)
        {
            this._Logger = logger;
            this.Listener.ConnectionRequestEvent += this.Listener_ConnectionRequestEvent;
            this.Listener.PeerConnectedEvent += this.Listener_PeerConnectedEvent;
            this.Listener.NetworkReceiveEvent += this.Listener_NetworkReceiveEvent;
            this.Listener.PeerDisconnectedEvent += this.Listener_PeerDisconnectedEvent;
        }
        #endregion

        #region Private Methods
        private void HandleRPC(string method, byte[] args, NetPeer? peer = null)
        {
            if (this._RegisteredRPCs.TryGetValue(method, out ServerRpcAction? value))
            {
                value(peer, args);
            }
        }

        private void Listener_ConnectionRequestEvent(ConnectionRequest request)
        {
            if (request.Data.UserDataSize == 0) return;
            byte[] bytes = request.Data.RawData[request.Data.UserDataOffset..(request.Data.UserDataOffset + request.Data.UserDataSize)];
            ConnectionData? data = MemoryPackSerializer.Deserialize<ConnectionData>(bytes);
            if (data == null) return;
            if (this._Players.Count == this.MaxPlayers)
            {
                string reason = "Server is full";
                request.Reject(Encoding.UTF8.GetBytes(reason));
                return;
            }
            string ip = request.RemoteEndPoint.Address.ToString();
            if (this._Banned.Contains(ip))
            {
                string reason = "You are banned from this server";
                request.Reject(Encoding.UTF8.GetBytes(reason));
                return;
            }
            if (data.Key == Constants.DEFAULT_KEY)
            {
                request.Accept();
                foreach (var state in data.InitStates)
                {
                    if (this.ReservedStates.Contains(state.Key)) continue;
                    if (state.Key.StartsWith("[player]"))
                    {
                        string ipPort = request.RemoteEndPoint.ToString();
                        if (!this._PlayerStatesToSet.ContainsKey(ipPort)) this._PlayerStatesToSet[ipPort] = [];
                        if (this._PlayerStatesToSet[ipPort].ContainsKey(state.Key)) this._PlayerStatesToSet[ipPort][state.Key] = state.Value;
                        this._PlayerStatesToSet[ipPort][state.Key.Replace("[player]", "")] = state.Value;
                    }
                    else this._States[state.Key] = state.Value;
                }
            }
            else
            {
                string reason = "Invalid key";
                request.Reject(Encoding.UTF8.GetBytes(reason));
            }
        }

        private void Listener_NetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            if (reader == null || reader.AvailableBytes > 0)
            {
                return;
            }

            string origin = this.GetPeerId(peer);
            byte[] bytes = reader.GetRemainingBytes();
            Packet? packet = MemoryPackSerializer.Deserialize<Packet>(bytes);

            if (packet == null)
            {
                return;
            }

            if (this.DataReceived != null)
            {
                packet = this.DataReceived(peer, packet);
            }

            switch (packet!.Type)
            {
                case PacketType.Ping:
                    if (packet.Target == "server")
                    {
                        peer.Send(bytes, deliveryMethod);
                    }
                    else
                    {
                        NetPeer? targetPeer = this._Players.FirstOrDefault(x => x.Key == packet.Target).Value;
                        if (origin != null && targetPeer != null)
                        {
                            if (packet.Key == "pong")
                            {
                                Packet pong = new()
                                {
                                    Type = PacketType.Ping,
                                    Target = packet.Target
                                };
                                targetPeer.Send(pong.Pack(), deliveryMethod);
                            }
                            else
                            {
                                Packet ping = new()
                                {
                                    Type = PacketType.Ping,
                                    Target = origin
                                };
                                targetPeer.Send(ping.Pack(), deliveryMethod);
                            }
                        }
                    }
                    break;
                case PacketType.SetState:
                    if (packet.Target.Length == 36)
                    {
                        if (packet.Target == origin)
                        {
                            this._States[origin + packet.Key] = packet.Data;
                            this.SyncState(packet, bytes, deliveryMethod, peer);
                        }
                    }
                    else
                    {
                        if (this.ReservedStates.Contains(packet.Key))
                        {
                            byte[] data = [];
                            if (this._States.TryGetValue(packet.Key, out byte[]? value)) data = value;
                            else this._States[packet.Key] = data;
                            Packet response = new(PacketType.SetState, "FORBIDDEN", packet.Key, data);
                            peer.Send(response.Pack(), DeliveryMethod.ReliableOrdered);
                        }
                        this._States[packet.Key] = packet.Data;
                        this.SyncState(packet, bytes, deliveryMethod, peer);
                    }
                    break;
                case PacketType.RPC:
                    if (this.GetPeer(packet.Target) != null)
                    {
                        this.SendRPC(bytes, packet.Target, RpcMode.Target, deliveryMethod);
                    }
                    else
                    {
                        string peerId = this.GetPeerId(peer);
                        switch (packet.Target)
                        {
                            case "self":
                                this.SendRPC(bytes, peerId, RpcMode.Target, deliveryMethod);
                                break;
                            case "all":
                                Packet allPacket = new(packet.Type, peerId, packet.Key, packet.Data);
                                this.SendRPC(allPacket.Pack(), "", RpcMode.All, deliveryMethod);
                                break;
                            case "others":
                                Packet othersPacket = new(packet.Type, peerId, packet.Key, packet.Data);
                                this.SendRPC(othersPacket.Pack(), peerId, RpcMode.Others, deliveryMethod);
                                break;
                            case "server":
                                this.HandleRPC(packet.Key, packet.Data, peer);
                                break;
                            default:
                                break;
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        private void Listener_PeerConnectedEvent(NetPeer peer)
        {
            string id = Guid.NewGuid().ToString();

            while (this._Players.ContainsKey(id))
            {
                id = Guid.NewGuid().ToString();
            }

            if (this.AllowAdminUsers && this._Players.IsEmpty)
            {
                this._Admins[id] = true;
            }

            string ipPort = peer.Address.ToString() + ":" + peer.Port;

            if (this._PlayerStatesToSet.TryGetValue(ipPort, out Dictionary<string, byte[]>? value))
            {
                foreach (var state in value)
                {
                    this._States[id + state.Key] = state.Value;
                }
                this._PlayerStatesToSet.TryRemove(ipPort, out _);
            }

            List<string> players = [id, .. this._Players.Keys];
            Packet packet1 = new(PacketType.PeerConnected, "", "Connected", Encoding.UTF8.GetBytes(id));
            this.SendAll(packet1.Pack(), DeliveryMethod.ReliableOrdered, peer);
            this._Players[id] = peer;
            byte[] data = MemoryPackSerializer.Serialize(players);
            Packet packet2 = new(PacketType.SelfConnected, "", "Connected", data);
            peer.Send(packet2.Pack(), DeliveryMethod.ReliableOrdered);
            byte[] states = MemoryPackSerializer.Serialize(this._States);
            Packet packet3 = new(PacketType.Init, "", "Init", states);

            peer.Send(packet3.Pack(), DeliveryMethod.ReliableOrdered);
        }

        private void Listener_PeerDisconnectedEvent(NetPeer peer, DisconnectInfo info)
        {
            string disconnectedId = this.GetPeerId(peer);
            this._Admins.TryRemove(disconnectedId, out _);
            Packet packet = new(PacketType.PeerDisconnected, "", "Disconnected", Encoding.UTF8.GetBytes(disconnectedId));
            this.SendAll(packet.Pack(), DeliveryMethod.ReliableOrdered, peer);
        }

        private async Task PollEvents()
        {
            while (true)
            {
                if (this._PollToken != null && this._PollToken.IsCancellationRequested)
                {
                    this.IsPolling = false;
                    break;
                }

                this.IsPolling = true;
                this.NetManager!.PollEvents();
                await Task.Delay(50);
            }
        }
        private void SendRPC(byte[] rawPacket, string target, RpcMode mode, DeliveryMethod method)
        {
            NetPeer? targetPeer = this.GetPeer(target);
            switch (mode)
            {
                case RpcMode.Target:
                    targetPeer?.Send(rawPacket, method);
                    break;
                case RpcMode.Others:
                    this.SendAll(rawPacket, method, targetPeer);
                    break;
                case RpcMode.All:
                    this.SendAll(rawPacket, method);
                    break;
                case RpcMode.Host:
                    targetPeer?.Send(rawPacket, method);
                    break;
            }
        }

        /// <summary>
        /// Syncs the state to all connected peers
        /// </summary>
        /// <param name="packet">The packet</param>
        /// <param name="rawPacket">The raw packet</param>
        /// <param name="method">The </param>
        /// <param name="origin">The peer from which the state originated from</param>
        private void SyncState(Packet packet, byte[] rawPacket, DeliveryMethod method = DeliveryMethod.ReliableUnordered, NetPeer? origin = null)
        {
            if (this.NetManager == null) return;
            if (origin == null)
            {
                foreach (NetPeer peer in this.NetManager.ConnectedPeerList)
                {
                    peer.Send(rawPacket, method);
                }
            }
            else
            {
                foreach (NetPeer peer in this.NetManager.ConnectedPeerList)
                {
                    if (peer != origin)
                    {
                        peer.Send(rawPacket, method);
                    }
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// A delegate for RPC actions on the server
        /// </summary>
        /// <param name="peer">The peer that invoked the RPC</param>
        /// <param name="args">The MemoryPacked arguments</param>
        public delegate void ServerRpcAction(NetPeer? peer, byte[] args);

        /// <summary>
        /// Returns a list of all the banned IP addresses
        /// </summary>
        public IEnumerable<string> Banned
        {
            get
            {
                return this._Banned;
            }
        }

        /// <summary>
        /// This function will be called when a packet is received. Return <c>null</c> to ignore the packet.
        /// </summary>
        public Func<NetPeer, Packet, Packet?>? DataReceived { get; private set; }

        /// <summary>
        /// Checks if a string is a valid GUID
        /// </summary>
        /// <param name="id"></param>
        /// <returns><c>true</c> if the string is a valid GUID, <c>false</c> otherwise</returns>
        public static bool IsGuid(string id)
        {
            if (id.Length != 36) return false;
            return PreCompiledRegex.GuidRegex().IsMatch(id);
        }

        /// <summary>
        /// Checks if a string starts with a GUID
        /// </summary>
        /// <param name="id"></param>
        /// <returns><c>true</c> if the string starts with a GUID, <c>false</c> otherwise</returns>
        public static bool StartsWithGuid(string id)
        {
            if (id.Length < 36) return false;
            return PreCompiledRegex.GuidRegex().IsMatch(id.AsSpan(0, 36));
        }

        /// <summary>
        /// Clears all global states except for the specified keys
        /// </summary>
        /// <param name="except">Keys to keep</param>
        public void ClearAllGlobalStates(IEnumerable<string> except)
        {
            var globalStates = this._States.Where(x => !PreCompiledRegex.GuidRegex().IsMatch(x.Key) && !except.Contains(x.Key)).ToList();
            foreach (var state in globalStates)
            {
                this._States.TryRemove(state.Key, out _);
            }
            Packet delete = new(PacketType.DeleteState, "global", "", MemoryPackSerializer.Serialize(except));
            this.SendAll(delete.Pack(), DeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Clears all player states except for the specified keys
        /// </summary>
        /// <param name="except">The keys to keep</param>
        public void ClearAllPlayerStates(IEnumerable<string> except)
        {
            var playerStates = this._States.Where(x => StartsWithGuid(x.Key) && !except.Contains(x.Key[..36])).ToList();
            foreach (var state in playerStates)
            {
                this._States.TryRemove(state.Key, out _);
            }
            Packet delete = new(PacketType.DeleteState, "players", "", MemoryPackSerializer.Serialize(except));
            this.SendAll(delete.Pack(), DeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Clears all player states for the specific player except for the specified keys
        /// </summary>
        /// <param name="player"></param>
        /// <param name="except">The keys to keep</param>
        public void ClearSpecificPlayerStates(string player, IEnumerable<string> except)
        {
            foreach (var key in except)
            {
                this._States.TryRemove(player + key, out _);
            }
            Packet delete = new(PacketType.DeleteState, player, "", MemoryPackSerializer.Serialize(except));
            this.SendAll(delete.Pack(), DeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Deletes the player state with the specified key
        /// </summary>
        /// <param name="player">The player id</param>
        /// <param name="key">The key to delete</param>
        public void DeletePlayerState(string player, string key)
        {
            this._States.TryRemove(player + key, out _);
            Packet delete = new(PacketType.DeleteState, player, key, [0]);
            this.SendAll(delete.Pack(), DeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Deletes the state with the specified key
        /// </summary>
        /// <param name="key">The key to delete</param>
        public void DeleteState(string key)
        {
            if (key.Length == 36 && this._States.ContainsKey(key[..36])) return;
            this._States.TryRemove(key, out _);
            Packet delete = new(PacketType.DeleteState, "global", key, [0]);
            this.SendAll(delete.Pack(), DeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Returns a list of all the players that are admins
        /// </summary>
        public IEnumerable<string> GetAdminList()
        {
            return [.. this._Admins.Keys];
        }
        /// <summary>
        /// Attempts to get a peer by id
        /// </summary>
        /// <param name="id">The target id</param>
        /// <returns>A <c>NetPeer</c> or <c>null</c> if not found</returns>
        public NetPeer? GetPeer(string id)
        {
            return this._Players.FirstOrDefault(x => x.Key == id).Value;
        }

        /// <summary>
        /// Gets the peer id from the peer
        /// </summary>
        /// <param name="peer">The target peer</param>
        /// <returns>The id of the peer, or <c>String.Empty</c></returns>
        public string GetPeerId(NetPeer peer)
        {
            return this._Players.FirstOrDefault(x => x.Value == peer).Key ?? string.Empty;
        }

        /// <summary>
        /// Bans a player from the server using their IP address
        /// </summary>
        /// <param name="id">The target player id</param>
        /// <param name="reason">The ban reason</param>
        public void IPBan(string id, string reason)
        {
            if (this._Players.TryGetValue(id, out NetPeer? value))
            {
                NetPeer peer = value;
                Packet packet = new(PacketType.Ban, "", "Banned", Encoding.UTF8.GetBytes(reason));
                this._Banned.Add(peer.Address.ToString().Split(':')[0]);
                peer.Send(packet.Pack(), DeliveryMethod.ReliableOrdered);
                peer.Disconnect(Encoding.UTF8.GetBytes("Banned: " + reason));
            }
        }

        /// <summary>
        /// Checks if a player is an admin
        /// </summary>
        /// <param name="id">The target id</param>
        /// <returns><c>true</c> if the player is an admin, <c>false</c> otherwise</returns>
        public bool IsAdmin(string id)
        {
            return this._Admins.ContainsKey(id);
        }

        /// <summary>
        /// Kicks a player from the server
        /// </summary>
        /// <param name="id">The target player id</param>
        /// <param name="reason">The kick reason</param>
        public void Kick(string id, string reason)
        {
            if (this._Players.TryGetValue(id, out NetPeer? value))
            {
                NetPeer peer = value;
                Packet packet = new(PacketType.Kick, "", "Kicked", Encoding.UTF8.GetBytes(reason));
                peer.Send(packet.Pack(), DeliveryMethod.ReliableOrdered);
                peer.Disconnect(Encoding.UTF8.GetBytes("Kicked: " + reason));
            }
        }

        /// <summary>
        /// Fluent version of <c>DataReceived</c>
        /// </summary>
        /// <param name="func">Function to call when a packet is received</param>
        /// <returns>This server</returns>
        public BetterServer OnDataReceived(Func<NetPeer, Packet, Packet?> func)
        {
            this.DataReceived = func;
            return this;
        }

        /// <summary>
        /// Registers a Remote Procedure Call with a method name and an action to invoke.
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="action">The method</param>
        /// <returns>This server</returns>
        public BetterServer RegisterRPC(string method, ServerRpcAction action)
        {
            this._RegisteredRPCs[method] = action;
            return this;
        }

        /// <summary>
        /// Calls a registered RPC on this server
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The arguments. Must be MemoryPackable</param>
        public void RpcSelf(string method, byte[] args)
        {
            this.HandleRPC(method, args);
        }

        /// <summary>
        /// Calls a registered RPC on this server
        /// </summary>
        /// <typeparam name="T">The type of the arguments. Must be MemoryPackable</typeparam>
        /// <param name="method">The name of the method</param>
        /// <param name="args">The arguments. Must be MemoryPackable</param>
        public void RpcSelf<T>(string method, T args)
        {
            byte[] bytes = MemoryPackSerializer.Serialize(args);
            this.HandleRPC(method, bytes);
        }

        /// <summary>
        /// Sends a packet to everyone except the specified peer
        /// </summary>
        /// <param name="data">The packet data</param>
        /// <param name="method">The delivery method</param>
        /// <param name="except">The peer to exclude</param>
        public void SendAll(byte[] data, DeliveryMethod method, NetPeer? except = null)
        {
            foreach (var player in this._Players.Where(player => player.Value != except))
            {
                player.Value.Send(data, method);
            }
        }

        /// <summary>
        /// Starts the server on the specified port<br/>
        /// creates a separate thread
        /// </summary>
        /// <param name="port">The port to start the server on. Default is 9050</param>
        /// <returns><c>true</c> if the server started successfully, <c>false</c> otherwise</returns>
        public bool Start(int port = 9050)
        {
            if (this.NetManager != null && this.NetManager.IsRunning)
            {
                return false;
            }

            this.NetManager = new(this.Listener);
            try
            {
                if (this.NetManager.Start(port))
                {
                    this._PollToken = new();

                    Task.Run(this.PollEvents);
                    return this.IsPolling;
                }
                else
                {
                    this.NetManager = null;
                    return false;
                }
            }
            catch
            {
                this.NetManager?.Stop();
                this.NetManager = null;
                return false;
            }
        }

        /// <summary>
        /// Stops the server
        /// </summary>
        public void Stop()
        {
            this._PollToken?.Cancel();

            if (this.NetManager == null)
            {
                return;
            }

            this._Players.Clear();
            this._Admins.Clear();
            this._States.Clear();
            this.NetManager?.Stop();
            this.NetManager = null;
        }

        /// <summary>
        /// Whether this server allows admin users
        /// </summary>
        /// <param name="allowAdminUsers"></param>
        /// <returns>This server</returns>
        public BetterServer WithAdminUsers()
        {
            this.AllowAdminUsers = true;
            return this;
        }

        /// <summary>
        /// Sets the banlist for the server
        /// </summary>
        /// <param name="addresses"></param>
        /// <returns>This server</returns>
        public BetterServer WithBannedUsers(IEnumerable<string> addresses)
        {
            this._Banned.AddRange(addresses);
            return this;
        }

        /// <summary>
        /// The max amount of players
        /// </summary>
        /// <param name="maxPlayers"></param>
        /// <returns>This server</returns>
        public BetterServer WithMaxPlayers(int maxPlayers)
        {
            this.MaxPlayers = maxPlayers;
            return this;
        }

        /// <summary>
        /// Sets the interval between polling events. Default is 15ms
        /// </summary>
        /// <param name="interval"></param>
        /// <returns>This server</returns>
        public BetterServer WithPollInterval(int interval)
        {
            this.PollInterval = interval;
            return this;
        }
        /// <summary>
        /// Sets the reserved states for the server
        /// </summary>
        /// <param name="states"></param>
        /// <returns></returns>
        public BetterServer WithReservedStates(IEnumerable<string> states)
        {
            this.ReservedStates = new(states);
            return this;
        }
        // Utils
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
                    this.Stop();
                    this._PollToken?.Dispose();
                }

                disposedValue = true;
            }
        }
        #endregion
    }
}