using System;
using System.Collections.Generic;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Main network manager that handles all networking operations
    /// </summary>
    public class NetworkManager : IDisposable
    {
        private NetworkTransport transport;
        private bool isInitialized;

        public bool IsHost => transport.IsHost;
        public bool IsClient => transport.IsClient;
        public bool IsConnected => transport.IsConnected;
        public uint LocalClientId { get; private set; }

        public event Action<uint>? OnClientConnected;
        public event Action<uint>? OnClientDisconnected;
        public event Action? OnConnectedToServer;
        public event Action? OnDisconnectedFromServer;

        public NetworkManager()
        {
            transport = new NetworkTransport();
            SetupTransportEvents();
        }

        private void SetupTransportEvents()
        {
            transport.OnClientConnected += (clientId) =>
            {
                OnClientConnected?.Invoke(clientId);

                // Sync existing network objects to the new client
                SyncNetworkObjectsToClient(clientId);
            };

            transport.OnClientDisconnected += (clientId) =>
            {
                OnClientDisconnected?.Invoke(clientId);
            };

            transport.OnConnectedToServer += () =>
            {
                LocalClientId = 1; // Client always gets ID 1
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
                LocalClientId = 0; // Server is always client ID 0
                isInitialized = true;
                NetworkBehaviour.Initialize(this);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Start as client and connect to server
        /// </summary>
        public bool StartClient(string serverAddress, int port)
        {
            if (transport.StartClient(serverAddress, port))
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
                // Check if it's a spawn message first
                if (NetworkObjectSpawner.IsSpawnMessage(data))
                {
                    NetworkObjectSpawner.HandleSpawnMessage(data);
                    return;
                }

                // Otherwise, handle as RPC
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
        /// This creates a proper proxy instance locally and sends spawn message to clients
        /// </summary>
        public T SpawnNetworkObject<T>() where T : NetworkBehaviour, new()
        {
            if (!IsHost)
                throw new InvalidOperationException("Only the server can spawn network objects");

            // Create proxy instance locally (like the spawner does)
            var networkObject = NetworkBehaviour.CreateProxy<T>();

            // Set network properties locally
            networkObject.SetNetworkProperties(networkObject.NetworkObjectId, LocalClientId);

            Console.WriteLine($"Server spawning network object: {typeof(T).Name} (ID: {networkObject.NetworkObjectId})");

            // Send spawn message to all clients
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
        public IReadOnlyDictionary<uint, NetworkClient> GetConnectedClients()
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