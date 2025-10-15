using System;
using Castle.DynamicProxy;
using System.Reflection;

namespace Netcode
{
    /// <summary>
    /// Attribute to mark methods that should be executed on all clients. Methods with this attribute MUST be declared as virtual
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute
    {
        public bool RequireOwnership { get; set; } = false;
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Reliable;
    }

    /// <summary>
    /// Attribute to mark methods that should be executed on the server. Methods with this attribute MUST be declared as virtual
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute
    {
        public bool RequireOwnership { get; set; } = true;
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Reliable;
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
                var methodName = method.Name;
                var parameters = invocation.Arguments ?? new object[0];

                if (networkBehaviour.InterceptRpc(parameters!, methodName))
                {
                    return;
                }
            }

            invocation.Proceed();
        }
    }
}