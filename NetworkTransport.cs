using System;
using System.Collections.Generic;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Represents a connected client
    /// </summary>
    public class NetworkClient : INetworkClient
    {
        public uint ClientId { get; }
        public NetPeer? Peer { get; }
        public bool IsConnected { get; set; }

        public NetworkClient(uint clientId, NetPeer? peer = null)
        {
            ClientId = clientId;
            Peer = peer;
            IsConnected = true;
        }
    }

    /// <summary>
    /// Network transport layer using LiteNetLib
    /// </summary>
    public class NetworkTransport : INetworkTransport, IDisposable, INetEventListener
    {
        private NetManager? netManager;
        private readonly Dictionary<uint, NetworkClient> clients = new();
        private readonly Dictionary<int, uint> peerIdToClientId = new(); // Map peer ID to client ID
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
        public bool IsConnected => (isServer && netManager != null && netManager.IsRunning) ||
                                  (isClient && serverConnection?.IsConnected == true);

        public IReadOnlyDictionary<uint, INetworkClient> ConnectedClients => (IReadOnlyDictionary<uint, INetworkClient>)clients;

        /// <summary>
        /// Start as server
        /// </summary>
        public bool StartServer(int port, int maxClients = 32)
        {
            if (netManager != null)
                return false;

            try
            {
                netManager = new NetManager(this)
                {
                    BroadcastReceiveEnabled = true,
                    UnconnectedMessagesEnabled = true
                };

                bool started = netManager.Start(port);
                if (started)
                {
                    isServer = true;
                }
                return started;
            }
            catch
            {
                netManager?.Stop();
                netManager = null;
                return false;
            }
        }

        /// <summary>
        /// Start as client and connect to server
        /// </summary>
        public bool StartClient(string serverAddress, int port)
        {
            if (netManager != null)
                return false;

            try
            {
                netManager = new NetManager(this);
                netManager.Start();

                var peer = netManager.Connect(serverAddress, port, "");
                if (peer != null)
                {
                    serverConnection = new NetworkClient(0, peer); // Server is always ID 0
                    isClient = true;
                    return true;
                }
                return false;
            }
            catch
            {
                netManager?.Stop();
                netManager = null;
                return false;
            }
        }

        /// <summary>
        /// Process network events
        /// </summary>
        public void Update()
        {
            netManager?.PollEvents();
        }

        // INetEventListener implementation
        public void OnPeerConnected(NetPeer peer)
        {
            if (isServer)
            {
                var clientId = nextClientId++;
                var client = new NetworkClient(clientId, peer);
                clients[clientId] = client;

                // Map peer ID to client ID
                peerIdToClientId[peer.Id] = clientId;

                OnClientConnected?.Invoke(clientId);
            }
            else if (isClient)
            {
                if (serverConnection != null)
                {
                    serverConnection.IsConnected = true;
                }
                OnConnectedToServer?.Invoke();
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (isServer)
            {
                if (peerIdToClientId.TryGetValue(peer.Id, out var clientId))
                {
                    if (clients.TryGetValue(clientId, out var client))
                    {
                        client.IsConnected = false;
                        clients.Remove(clientId);
                        peerIdToClientId.Remove(peer.Id);
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

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            var dataLength = reader.AvailableBytes;
            var data = new byte[dataLength];
            reader.GetBytes(data, dataLength);

            var senderId = isServer ?
                (peerIdToClientId.TryGetValue(peer.Id, out var id) ? id : 0) : 0;

            OnDataReceived?.Invoke(data, senderId);
        }

        public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
            // Handle network errors if needed
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            // Handle latency updates if needed
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (isServer)
            {
                request.Accept();
            }
            else
            {
                request.Reject();
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Handle unconnected messages if needed
        }

        /// <summary>
        /// Send data to a specific client (server only)
        /// </summary>
        public void SendToClient(uint clientId, byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            if (!isServer || netManager == null)
                return;

            if (clients.TryGetValue(clientId, out var client) && client.IsConnected && client.Peer != null)
            {
                client.Peer.Send(data, GetDeliveryMethod(deliveryMode));
            }
        }

        /// <summary>
        /// Send data to all clients (server only)
        /// </summary>
        public void SendToAllClients(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            if (!isServer || netManager == null)
                return;

            var deliveryMethod = GetDeliveryMethod(deliveryMode);
            foreach (var client in clients.Values)
            {
                if (client.IsConnected && client.Peer != null)
                {
                    client.Peer.Send(data, deliveryMethod);
                }
            }
        }

        /// <summary>
        /// Send data to server (client only)
        /// </summary>
        public void SendToServer(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            if (!isClient || netManager == null || serverConnection?.IsConnected != true || serverConnection.Peer == null)
                return;

            serverConnection.Peer.Send(data, GetDeliveryMethod(deliveryMode));
        }

        private static DeliveryMethod GetDeliveryMethod(DeliveryMode deliveryMode)
        {
            return deliveryMode switch
            {
                DeliveryMode.Reliable => DeliveryMethod.ReliableOrdered,
                DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
                DeliveryMode.UnreliableSequenced => DeliveryMethod.Sequenced,
                _ => DeliveryMethod.ReliableOrdered
            };
        }

        /// <summary>
        /// Stop and cleanup
        /// </summary>
        public void Stop()
        {
            if (netManager != null)
            {
                if (isServer)
                {
                    // Disconnect all clients
                    foreach (var client in clients.Values)
                    {
                        if (client.IsConnected && client.Peer != null)
                        {
                            client.Peer.Disconnect();
                        }
                    }
                }
                else if (isClient && serverConnection?.IsConnected == true && serverConnection.Peer != null)
                {
                    serverConnection.Peer.Disconnect();
                }

                netManager.Stop();
                netManager = null;
            }

            clients.Clear();
            peerIdToClientId.Clear();
            serverConnection = null;
            isServer = false;
            isClient = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}