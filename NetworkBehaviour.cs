using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using Castle.DynamicProxy;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Base class for networked objects that can send and receive RPCs
    /// </summary>
    public abstract class NetworkBehaviour
    {
        private static uint nextNetworkId = 1;
        private static readonly Dictionary<uint, NetworkBehaviour> NetworkObjects = new();
        private static readonly Dictionary<Type, Dictionary<string, MethodInfo>> CachedRpcMethods = new();
        private readonly Dictionary<string, bool> methodInterceptionCache = new();

        public uint NetworkObjectId { get; private set; }
        public uint OwnerClientId { get; set; }

        /// <summary>
        /// Returns true if this client is acting as the server
        /// </summary>
        public bool IsServer => NetworkManager?.IsHost ?? false;

        /// <summary>
        /// Returns true if the local client owns this network object
        /// </summary>
        public bool IsOwner => NetworkManager?.LocalClientId == OwnerClientId;

        /// <summary>
        /// Reference to the network manager
        /// </summary>
        protected static NetworkManager? NetworkManager { get; set; }


        //-------------- Interception and RPC injection --------------
        private static readonly ProxyGenerator _proxyGenerator = new ProxyGenerator();
        private static readonly RpcInterceptor _rpcInterceptor = new RpcInterceptor();

        /// <summary>
        /// Creates a proxy instance that will intercept RPC calls
        /// </summary>
        public static T CreateProxy<T>() where T : NetworkBehaviour, new()
        {
            return _proxyGenerator.CreateClassProxy<T>(_rpcInterceptor);
        }

        protected NetworkBehaviour()
        {
            NetworkObjectId = nextNetworkId++;
            NetworkObjects[NetworkObjectId] = this;

            // Cache RPC methods for this type
            CacheRpcMethods();
        }

        /// <summary>
        /// Set network properties (used when spawning from network)
        /// </summary>
        internal void SetNetworkProperties(uint networkObjectId, uint ownerClientId)
        {
            // Remove from old ID if it exists
            if (NetworkObjects.ContainsKey(NetworkObjectId))
                NetworkObjects.Remove(NetworkObjectId);

            NetworkObjectId = networkObjectId;
            OwnerClientId = ownerClientId;
            NetworkObjects[NetworkObjectId] = this;

            // Call the virtual spawn method for custom spawn logic
            OnNetworkSpawn();
        }

        /// <summary>
        /// Called when this object is spawned over the network.
        /// Override this method to implement custom spawn behavior.
        /// </summary>
        public virtual void OnNetworkSpawn()
        {
            // Default implementation - can be overridden by derived classes
        }

        /// <summary>
        /// Initialize the network behaviour with a network manager
        /// </summary>
        public static void Initialize(NetworkManager networkManager)
        {
            NetworkManager = networkManager;
        }

        /// <summary>
        /// Cache all RPC methods for this object type and validate they are virtual
        /// </summary>
        private void CacheRpcMethods()
        {
            var type = GetType();
            if (CachedRpcMethods.ContainsKey(type))
                return;

            var methods = new Dictionary<string, MethodInfo>();
            var allMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var method in allMethods)
            {
                var clientRpcAttr = method.GetCustomAttribute<ClientRpcAttribute>();
                var serverRpcAttr = method.GetCustomAttribute<ServerRpcAttribute>();

                if (clientRpcAttr != null || serverRpcAttr != null)
                {
                    // Validate that RPC methods are virtual
                    if (!method.IsVirtual || method.IsFinal)
                    {
                        throw new InvalidOperationException(
                            $"RPC method '{method.Name}' in class '{type.Name}' must be declared as virtual. " +
                            $"All methods with [ClientRpc] or [ServerRpc] attributes must be virtual to enable proxy interception.");
                    }

                    methods[method.Name] = method;
                }
            }

            CachedRpcMethods[type] = methods;
        }

        /// <summary>
        /// Execute an RPC method on this network object
        /// </summary>
        internal static bool ExecuteRpc(uint networkObjectId, string methodName, object[] parameters, uint senderId)
        {
            if (!NetworkObjects.TryGetValue(networkObjectId, out var networkObject))
                return false;

            var type = networkObject.GetType();
            if (!CachedRpcMethods.TryGetValue(type, out var methods) ||
                !methods.TryGetValue(methodName, out var method))
                return false;

            // Check if this is a server RPC and validate ownership if required
            var serverRpcAttr = method.GetCustomAttribute<ServerRpcAttribute>();
            if (serverRpcAttr != null)
            {
                if (NetworkManager?.IsHost != true)
                    return false; // Server RPCs can only be executed on the server

                if (serverRpcAttr.RequireOwnership && networkObject.OwnerClientId != senderId)
                    return false; // Ownership check failed
            }

            // Check if this is a client RPC
            var clientRpcAttr = method.GetCustomAttribute<ClientRpcAttribute>();
            if (clientRpcAttr != null)
            {
                if (NetworkManager?.IsClient != true)
                    return false; // Client RPCs can only be executed on clients
            }

            try
            {
                // Convert parameters to match method signature
                var methodParams = method.GetParameters();
                var convertedParams = new object[methodParams.Length];

                for (int i = 0; i < Math.Min(parameters.Length, methodParams.Length); i++)
                {
                    convertedParams[i] = Convert.ChangeType(parameters[i], methodParams[i].ParameterType);
                }

                method.Invoke(networkObject, convertedParams);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing RPC {methodName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send an RPC to all clients (server only)
        /// </summary>
        protected void InvokeClientRpc(string methodName, params object[] parameters)
        {

            if (NetworkManager?.IsHost != true)
                return;

            var method = GetRpcMethod(methodName);
            var clientRpcAttr = method?.GetCustomAttribute<ClientRpcAttribute>();
            if (clientRpcAttr == null)
                return;

            // Check ownership if required
            if (clientRpcAttr.RequireOwnership && NetworkManager.LocalClientId != OwnerClientId)
                return;

            var data = RpcSerializer.SerializeRpcCall(methodName, NetworkObjectId, parameters);
            NetworkManager.SendToAllClients(data, clientRpcAttr.DeliveryMode);
        }

        /// <summary>
        /// Send an RPC to a specific client (server only)
        /// </summary>
        protected void InvokeClientRpc(uint targetClientId, string methodName, params object[] parameters)
        {
            if (NetworkManager?.IsHost != true)
                return;

            var method = GetRpcMethod(methodName);
            var clientRpcAttr = method?.GetCustomAttribute<ClientRpcAttribute>();
            if (clientRpcAttr == null)
                return;

            // Check ownership if required
            if (clientRpcAttr.RequireOwnership && NetworkManager.LocalClientId != OwnerClientId)
                return;

            var data = RpcSerializer.SerializeRpcCall(methodName, NetworkObjectId, parameters);
            NetworkManager.SendToClient(targetClientId, data, clientRpcAttr.DeliveryMode);
        }

        /// <summary>
        /// Send an RPC to the server (client only)
        /// </summary>
        protected void InvokeServerRpc(string methodName, params object[] parameters)
        {
            Console.WriteLine($"InvokeServerRpc called for method: {methodName}");
            Console.WriteLine($"NetworkManager IsClient: {NetworkManager?.IsClient}, IsConnected: {NetworkManager?.IsConnected}");

            if (NetworkManager?.IsClient != true)
            {
                Console.WriteLine("Cannot send ServerRpc - NetworkManager is not a client or is null");
                return;
            }

            var method = GetRpcMethod(methodName);
            var serverRpcAttr = method?.GetCustomAttribute<ServerRpcAttribute>();
            if (serverRpcAttr == null)
            {
                Console.WriteLine($"Method {methodName} does not have ServerRpc attribute");
                return;
            }

            // Check ownership if required
            if (serverRpcAttr.RequireOwnership && NetworkManager.LocalClientId != OwnerClientId)
            {
                Console.WriteLine($"Ownership check failed - LocalClientId: {NetworkManager.LocalClientId}, OwnerClientId: {OwnerClientId}");
                return;
            }

            Console.WriteLine($"Serializing and sending ServerRpc {methodName} to server...");
            var data = RpcSerializer.SerializeRpcCall(methodName, NetworkObjectId, parameters);
            NetworkManager.SendToServer(data, serverRpcAttr.DeliveryMode);
            Console.WriteLine($"ServerRpc {methodName} sent successfully");
        }

        /// <summary>
        /// Intercept and automatically handle RPC method calls
        /// Call this method at the beginning of any method that should be handled as RPC
        /// </summary>
        public bool InterceptRpc(object[] parameters, [CallerMemberName] string methodName = "")
        {
            if (string.IsNullOrEmpty(methodName))
                return false;

            Console.WriteLine($"InterceptRpc called for method: {methodName}");
            Console.WriteLine($"NetworkManager state - IsHost: {NetworkManager?.IsHost}, IsClient: {NetworkManager?.IsClient}, IsConnected: {NetworkManager?.IsConnected}");

            // Check cache first
            if (methodInterceptionCache.TryGetValue(methodName, out var isRpcCached))
            {
                if (!isRpcCached)
                {
                    Console.WriteLine($"Method {methodName} is not an RPC (cached)");
                    return false;
                }
            }
            else
            {
                // Check if this method has RPC attributes
                var method = GetRpcMethod(methodName);
                var isRpc = method != null &&
                          (method.GetCustomAttribute<ServerRpcAttribute>() != null ||
                           method.GetCustomAttribute<ClientRpcAttribute>() != null);

                methodInterceptionCache[methodName] = isRpc;

                if (!isRpc)
                {
                    Console.WriteLine($"Method {methodName} is not an RPC");
                    return false;
                }
            }

            var rpcMethod = GetRpcMethod(methodName);
            if (rpcMethod == null)
            {
                Console.WriteLine($"Could not find RPC method: {methodName}");
                return false;
            }

            // Handle ServerRpc
            var serverRpcAttr = rpcMethod.GetCustomAttribute<ServerRpcAttribute>();
            if (serverRpcAttr != null)
            {
                Console.WriteLine($"Method {methodName} is a ServerRpc");

                // If we're on the server, execute locally
                if (NetworkManager?.IsHost == true)
                {
                    Console.WriteLine("Executing ServerRpc locally on server/host");
                    return false; // Let the method execute normally on server
                }
                // If we're a client, send to server
                else if (NetworkManager?.IsClient == true)
                {
                    Console.WriteLine("Sending ServerRpc from client to server");
                    InvokeServerRpc(methodName, parameters);
                    return true; // Intercept - don't execute locally
                }
                else
                {
                    Console.WriteLine("NetworkManager is null or not connected - cannot send ServerRpc");
                    return false;
                }
            }

            // Handle ClientRpc  
            var clientRpcAttr = rpcMethod.GetCustomAttribute<ClientRpcAttribute>();
            if (clientRpcAttr != null)
            {
                Console.WriteLine($"Method {methodName} is a ClientRpc");

                // If we're on the server, send to all clients
                if (NetworkManager?.IsHost == true)
                {
                    Console.WriteLine("Sending ClientRpc from server to all clients");
                    InvokeClientRpc(methodName, parameters);
                    return false; // Also execute locally on server
                }
                // If we're a client, this was called remotely, execute normally
                else if (NetworkManager?.IsClient == true)
                {
                    Console.WriteLine("Executing ClientRpc locally on client");
                    return false; // Execute locally on client
                }
            }

            Console.WriteLine($"No valid network state for method {methodName}");
            return false;
        }

        /// <summary>
        /// Get an RPC method by name
        /// </summary>
        private MethodInfo? GetRpcMethod(string methodName)
        {
            var type = GetType();
            return CachedRpcMethods.TryGetValue(type, out var methods) &&
                   methods.TryGetValue(methodName, out var method) ? method : null;
        }

        /// <summary>
        /// Cleanup when object is destroyed
        /// </summary>
        public virtual void OnDestroy()
        {
            NetworkObjects.Remove(NetworkObjectId);
        }

        /// <summary>
        /// Get a network object by ID
        /// </summary>
        public static NetworkBehaviour? GetNetworkObject(uint networkObjectId)
        {
            return NetworkObjects.TryGetValue(networkObjectId, out var obj) ? obj : null;
        }

        /// <summary>
        /// Get all network objects
        /// </summary>
        public static IEnumerable<NetworkBehaviour> GetAllNetworkObjects()
        {
            return NetworkObjects.Values;
        }
    }
}