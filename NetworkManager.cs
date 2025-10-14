using System;
using System.Collections.Generic;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Main network manager that handles all networking operations
    /// </summary>
    public class NetworkManager : IDisposable
    {
        private INetworkTransport transport;
        private bool isInitialized;

        public bool IsHost => transport.IsHost;
        public bool IsClient => transport.IsClient;
        public bool IsConnected => transport.IsConnected;
        public uint LocalClientId { get; private set; }

        public event Action<uint>? OnClientConnected;
        public event Action<uint>? OnClientDisconnected;
        public event Action? OnConnectedToServer;
        public event Action? OnDisconnectedFromServer;

        public NetworkManager(INetworkTransport? customTransport)
        {
            if (customTransport == null)
                transport = new NetworkTransport();
            else
                transport = customTransport;
            SetupTransportEvents();
        }

        /// <summary>
        /// Setup transport event handlers
        /// </summary>
        private void SetupTransportEvents()
        {
            transport.OnClientConnected += (clientId) =>
            {
                OnClientConnected?.Invoke(clientId);
                SyncNetworkObjectsToClient(clientId);
                // Send assigned client ID to the newly connected client
                var idMsg = new byte[5];
                idMsg[0] = 0x01; // Message type: 1 = client ID assignment
                BitConverter.GetBytes(clientId).CopyTo(idMsg, 1);
                SendToClient(clientId, idMsg, DeliveryMode.Reliable);
            };

            transport.OnClientDisconnected += (clientId) =>
            {
                OnClientDisconnected?.Invoke(clientId);
            };

            transport.OnConnectedToServer += () =>
            {
                OnConnectedToServer?.Invoke();
            };

            transport.OnDisconnectedFromServer += () =>
            {
                OnDisconnectedFromServer?.Invoke();
            };

            transport.OnDataReceived += HandleDataReceived;
        }

        /// <summary>
        /// Start as server
        /// </summary>
        public bool StartServer(int port, int maxClients = 32)
        {
            if (transport.StartServer(port, maxClients))
            {
                LocalClientId = 0;
                isInitialized = true;
                NetworkBehaviour.Initialize(this);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Start as client and connect to server
        /// </summary>
        public bool StartClient(IHostIdentifier hostIdentifier, int port)
        {
            if (transport.StartClient(hostIdentifier, port))
            {
                isInitialized = true;
                NetworkBehaviour.Initialize(this);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update network state - call this every frame
        /// </summary>
        public void Update()
        {
            if (isInitialized)
            {
                transport.Update();
            }
        }

        /// <summary>
        /// Handle incoming network data
        /// </summary>
        private void HandleDataReceived(byte[] data, uint senderId)
        {
            try
            {
                // Check for client ID assignment message
                if (data.Length == 5 && data[0] == 0x01)
                {
                    uint assignedId = BitConverter.ToUInt32(data, 1);
                    LocalClientId = assignedId;
                    Console.WriteLine($"Assigned LocalClientId: {LocalClientId}");
                    OnConnectedToServer?.Invoke();
                    return;
                }

                // Ignore connection trigger message
                if (data.Length == 1 && data[0] == 0x00)
                {
                    return;
                }

                if (NetworkObjectSpawner.IsSpawnMessage(data))
                {
                    NetworkObjectSpawner.HandleSpawnMessage(data);
                    return;
                }

                var (methodName, networkObjectId, parameters) = RpcSerializer.DeserializeRpcCall(data);
                NetworkBehaviour.ExecuteRpc(networkObjectId, methodName, parameters, senderId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling network data: {ex.Message}");
            }
        }

        /// <summary>
        /// Send data to a specific client (server only)
        /// </summary>
        public void SendToClient(uint clientId, byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            transport.SendToClient(clientId, data, deliveryMode);
        }

        /// <summary>
        /// Send data to all clients (server only)
        /// </summary>
        public void SendToAllClients(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            transport.SendToAllClients(data, deliveryMode);
        }

        /// <summary>
        /// Send data to server (client only)
        /// </summary>
        public void SendToServer(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable)
        {
            transport.SendToServer(data, deliveryMode);
        }
        /// <summary>
        /// Spawn a network object of type T on all clients (server only)
        /// </summary>
        public T SpawnNetworkObject<T>() where T : NetworkBehaviour, new()
        {
            if (!IsHost)
                throw new InvalidOperationException("Only the server can spawn network objects");

            var networkObject = NetworkBehaviour.CreateProxy<T>();
            networkObject.SetNetworkProperties(networkObject.NetworkObjectId, 0);

            var spawnData = NetworkObjectSpawner.SerializeSpawnObject(networkObject);
            SendToAllClients(spawnData, DeliveryMode.Reliable);

            return networkObject;
        }

        public T SpawnNetworkObject<T>(uint ownerClientId) where T : NetworkBehaviour, new()
        {
            if (!IsHost && ownerClientId != LocalClientId)
                throw new InvalidOperationException($"Only the server or the owning client can spawn network objects. passed: {ownerClientId}, expected: {LocalClientId}");

            var networkObject = NetworkBehaviour.CreateProxy<T>();
            networkObject.SetNetworkProperties(networkObject.NetworkObjectId, ownerClientId);

            var spawnData = NetworkObjectSpawner.SerializeSpawnObject(networkObject);
            SendToAllClients(spawnData, DeliveryMode.Reliable);

            return networkObject;
        }

        /// <summary>
        /// Send existing network objects to a newly connected client (server only)
        /// </summary>
        public void SyncNetworkObjectsToClient(uint clientId)
        {
            if (!IsHost) return;

            var existingObjects = NetworkBehaviour.GetAllNetworkObjects();
            foreach (var obj in existingObjects)
            {
                var spawnData = NetworkObjectSpawner.SerializeSpawnObject(obj);
                SendToClient(clientId, spawnData, DeliveryMode.Reliable);
            }
        }

        /// <summary>
        /// Stop networking
        /// </summary>
        public void Stop()
        {
            if (isInitialized)
            {
                transport.Stop();
                isInitialized = false;
            }
        }

        /// <summary>
        /// Get all connected clients
        /// </summary>
        public IReadOnlyDictionary<uint, INetworkClient> GetConnectedClients()
        {
            return transport.ConnectedClients;
        }

        public void Dispose()
        {
            Stop();
            transport?.Dispose();
        }
    }
}