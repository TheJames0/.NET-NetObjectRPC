using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using Castle.DynamicProxy;

namespace Netcode
{
    /// <summary>
    /// Base class for networked objects that can send and receive RPCs
    /// </summary>
    public abstract class NetworkBehaviour
    {
        public uint LocalClientId => NetworkManager?.LocalClientId ?? 0;
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
            CacheRpcMethods();
        }

        /// <summary>
        /// Set network properties (used when spawning from network)
        /// </summary>
        internal void SetNetworkProperties(uint networkObjectId, uint ownerClientId)
        {
            if (NetworkObjects.ContainsKey(NetworkObjectId))
                NetworkObjects.Remove(NetworkObjectId);

            NetworkObjectId = networkObjectId;
            OwnerClientId = ownerClientId;
            NetworkObjects[NetworkObjectId] = this;

            OnNetworkSpawn();
        }

        /// <summary>
        /// Called when this object is spawned over the network
        /// </summary>
        public virtual void OnNetworkSpawn()
        {
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

            var serverRpcAttr = method.GetCustomAttribute<ServerRpcAttribute>();
            if (serverRpcAttr != null)
            {
                if (NetworkManager?.IsHost != true)
                    return false;

                if (serverRpcAttr.RequireOwnership && networkObject.OwnerClientId != senderId)
                    return false;
            }

            var clientRpcAttr = method.GetCustomAttribute<ClientRpcAttribute>();
            if (clientRpcAttr != null)
            {
                if (NetworkManager?.IsClient != true)
                    return false;
            }

            try
            {
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
            if (NetworkManager?.IsClient != true)
                return;

            var method = GetRpcMethod(methodName);
            var serverRpcAttr = method?.GetCustomAttribute<ServerRpcAttribute>();
            if (serverRpcAttr == null)
                return;

            if (serverRpcAttr.RequireOwnership && NetworkManager.LocalClientId != OwnerClientId)
                return;

            var data = RpcSerializer.SerializeRpcCall(methodName, NetworkObjectId, parameters);
            NetworkManager.SendToServer(data, serverRpcAttr.DeliveryMode);
        }

        /// <summary>
        /// Intercept and automatically handle RPC method calls
        /// </summary>
        public bool InterceptRpc(object[] parameters, [CallerMemberName] string methodName = "")
        {
            if (string.IsNullOrEmpty(methodName))
                return false;

            if (methodInterceptionCache.TryGetValue(methodName, out var isRpcCached))
            {
                if (!isRpcCached)
                    return false;
            }
            else
            {
                var method = GetRpcMethod(methodName);
                var isRpc = method != null &&
                          (method.GetCustomAttribute<ServerRpcAttribute>() != null ||
                           method.GetCustomAttribute<ClientRpcAttribute>() != null);

                methodInterceptionCache[methodName] = isRpc;

                if (!isRpc)
                    return false;
            }

            var rpcMethod = GetRpcMethod(methodName);
            if (rpcMethod == null)
                return false;

            var serverRpcAttr = rpcMethod.GetCustomAttribute<ServerRpcAttribute>();
            if (serverRpcAttr != null)
            {
                if (NetworkManager?.IsHost == true)
                {
                    return false;
                }
                else if (NetworkManager?.IsClient == true)
                {
                    InvokeServerRpc(methodName, parameters);
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var clientRpcAttr = rpcMethod.GetCustomAttribute<ClientRpcAttribute>();
            if (clientRpcAttr != null)
            {
                if (NetworkManager?.IsHost == true)
                {
                    InvokeClientRpc(methodName, parameters);
                    return false;
                }
                else if (NetworkManager?.IsClient == true)
                {
                    return false;
                }
            }

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