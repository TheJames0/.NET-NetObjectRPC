using System;
using System.Collections.Generic;
using System.Net;
using ENet;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Represents a connected client
    /// </summary>
    public class NetworkClient
    {
        public uint ClientId { get; }
        public Peer Peer { get; }
        public bool IsConnected { get; set; }

        public NetworkClient(uint clientId, Peer peer)
        {
            ClientId = clientId;
            Peer = peer;
            IsConnected = true;
        }
    }

    /// <summary>
    /// Network transport layer using ENet
    /// </summary>
    public class NetworkTransport : IDisposable
    {
        private Host? host;
        private readonly Dictionary<uint, NetworkClient> clients = new();
        private readonly Dictionary<uint, uint> peerIdToClientId = new(); // Map peer ID to client ID
        private uint nextClientId = 1;
        private bool isServer;
        private bool isClient;
        private NetworkClient? serverConnection;

        public event Action<uint>? OnClientConnected;
        public event Action<uint>? OnClientDisconnected;
        public event Action<byte[], uint>? OnDataReceived;
        public event Action? OnConnectedToServer;
        public event Action? OnDisconnectedFromServer;

        public bool IsHost => isServer;
        public bool IsClient => isClient;
        public bool IsConnected => (isServer && host != null) || (isClient && serverConnection?.IsConnected == true);

        public IReadOnlyDictionary<uint, NetworkClient> ConnectedClients => clients;

        /// <summary>
        /// Start as server
        /// </summary>
        public bool StartServer(int port, int maxClients = 32)
        {
            if (host != null)
                return false;

            try
            {
                Library.Initialize();

                var address = new Address
                {
                    Port = (ushort)port
                };

                host = new Host();
                host.Create(address, maxClients);

                isServer = true;
                return true;
            }
            catch
            {
                host?.Dispose();
                host = null;
                return false;
            }
        }

        /// <summary>
        /// Start as client and connect to server
        /// </summary>
        public bool StartClient(string serverAddress, int port)
        {
            if (host != null)
                return false;

            try
            {
                Library.Initialize();

                host = new Host();
                host.Create();

                var address = new Address();
                address.SetHost(serverAddress);
                address.Port = (ushort)port;

                var peer = host.Connect(address);
                serverConnection = new NetworkClient(0, peer); // Server is always ID 0

                isClient = true;
                return true;
            }
            catch
            {
                host?.Dispose();
                host = null;
                return false;
            }
        }

        /// <summary>
        /// Process network events
        /// </summary>
        public void Update()
        {
            if (host == null)
                return;

            bool polled = false;
            while (!polled)
            {
                if (host.CheckEvents(out Event networkEvent) <= 0)
                {
                    if (host.Service(0, out networkEvent) <= 0)
                        break;
                    polled = true;
                }

                ProcessEvent(networkEvent);
            }
        }

        private void ProcessEvent(Event networkEvent)
        {
            switch (networkEvent.Type)
            {
                case EventType.Connect:
                    HandleConnect(networkEvent);
                    break;
                case EventType.Disconnect:
                    HandleDisconnect(networkEvent);
                    break;
                case EventType.Receive:
                    HandleReceive(networkEvent);
                    break;
            }
        }

        private void HandleConnect(Event networkEvent)
        {
            if (isServer)
            {
                var clientId = nextClientId++;
                var client = new NetworkClient(clientId, networkEvent.Peer);
                clients[clientId] = client;

                // Map peer ID to client ID
                peerIdToClientId[networkEvent.Peer.ID] = clientId;

                OnClientConnected?.Invoke(clientId);
            }
            else if (isClient)
            {
                OnConnectedToServer?.Invoke();
            }
        }

        private void HandleDisconnect(Event networkEvent)
        {
            if (isServer)
            {
                if (peerIdToClientId.TryGetValue(networkEvent.Peer.ID, out var clientId))
                {
                    if (clients.TryGetValue(clientId, out var client))
                    {
                        client.IsConnected = false;
                        clients.Remove(clientId);
                        peerIdToClientId.Remove(networkEvent.Peer.ID);
                        OnClientDisconnected?.Invoke(clientId);
                    }
                }
            }
            else if (isClient)
            {
                if (serverConnection != null)
                {
                    serverConnection.IsConnected = false;
                }
                OnDisconnectedFromServer?.Invoke();
            }
        }

        private void HandleReceive(Event networkEvent)
        {
            var packet = networkEvent.Packet;
            var dataLength = (int)packet.Length;
            var data = new byte[dataLength];

            // Copy packet data
            var packetData = packet.Data;
            System.Runtime.InteropServices.Marshal.Copy(packetData, data, 0, dataLength);

            var senderId = isServer ?
                (peerIdToClientId.TryGetValue(networkEvent.Peer.ID, out var id) ? id : 0) : 0;

            OnDataReceived?.Invoke(data, senderId);

            packet.Dispose();
        }

        /// <summary>
        /// Send data to a specific client (server only)
        /// </summary>
        public void SendToClient(uint clientId, byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            if (!isServer || host == null)
                return;

            if (clients.TryGetValue(clientId, out var client) && client.IsConnected)
            {
                var packet = default(Packet);
                packet.Create(data, GetPacketFlags(deliveryMode));
                client.Peer.Send(0, ref packet);
            }
        }

        /// <summary>
        /// Send data to all clients (server only)
        /// </summary>
        public void SendToAllClients(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            if (!isServer || host == null)
                return;

            var packet = default(Packet);
            packet.Create(data, GetPacketFlags(deliveryMode));

            foreach (var client in clients.Values)
            {
                if (client.IsConnected)
                {
                    client.Peer.Send(0, ref packet);
                }
            }
        }

        /// <summary>
        /// Send data to server (client only)
        /// </summary>
        public void SendToServer(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            if (!isClient || host == null || serverConnection?.IsConnected != true)
                return;

            var packet = default(Packet);
            packet.Create(data, GetPacketFlags(deliveryMode));
            serverConnection.Peer.Send(0, ref packet);
        }

        private static PacketFlags GetPacketFlags(DeliveryMode deliveryMode)
        {
            return deliveryMode switch
            {
                DeliveryMode.Reliable => PacketFlags.Reliable,
                DeliveryMode.Unreliable => PacketFlags.None,
                DeliveryMode.UnreliableSequenced => PacketFlags.Unsequenced,
                _ => PacketFlags.Reliable
            };
        }

        /// <summary>
        /// Stop and cleanup
        /// </summary>
        public void Stop()
        {
            if (host != null)
            {
                if (isServer)
                {
                    // Disconnect all clients
                    foreach (var client in clients.Values)
                    {
                        if (client.IsConnected)
                        {
                            client.Peer.DisconnectNow(0);
                        }
                    }
                }
                else if (isClient && serverConnection?.IsConnected == true)
                {
                    serverConnection.Peer.DisconnectNow(0);
                }

                host.Flush();
                host.Dispose();
                host = null;
            }

            clients.Clear();
            peerIdToClientId.Clear();
            serverConnection = null;
            isServer = false;
            isClient = false;

            Library.Deinitialize();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}