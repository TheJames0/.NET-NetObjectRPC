using System;
using Castle.DynamicProxy;
using System.Reflection;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Attribute to mark methods that should be executed on all clients
    /// Methods with this attribute MUST be declared as virtual
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute
    {
        public bool RequireOwnership { get; set; } = false;
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Reliable;

        public ClientRpcAttribute()
        {
            // Constructor - validation will be done in NetworkBehaviour.CacheRpcMethods
        }
    }

    /// <summary>
    /// Attribute to mark methods that should be executed on the server
    /// Methods with this attribute MUST be declared as virtual
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute
    {
        public bool RequireOwnership { get; set; } = true;
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Reliable;

        public ServerRpcAttribute()
        {
            // Constructor - validation will be done in NetworkBehaviour.CacheRpcMethods
        }
    }

    /// <summary>
    /// Delivery mode for network messages
    /// </summary>
    public enum DeliveryMode
    {
        Reliable,
        Unreliable,
        UnreliableSequenced
    }

    /// <summary>
    /// Interceptor that handles RPC method calls
    /// </summary>
    public class RpcInterceptor : IInterceptor
    {
        public void Intercept(IInvocation invocation)
        {
            var method = invocation.Method;
            bool hasRpcAttribute = method.GetCustomAttribute<ClientRpcAttribute>() != null ||
                                   method.GetCustomAttribute<ServerRpcAttribute>() != null;


            if (hasRpcAttribute && invocation.InvocationTarget is NetworkBehaviour networkBehaviour)
            {
                // Call InterceptRpc with the method arguments and method name
                var methodName = method.Name;
                var parameters = invocation.Arguments ?? new object[0];


                if (networkBehaviour.InterceptRpc(parameters!, methodName))
                {
                    // If intercepted, don't proceed with the original method
                    return;
                }

            }

            // Proceed with the original method call
            invocation.Proceed();
        }
    }
}