using System;
using System.Collections.Generic;

namespace Netcode
{
    public interface INetworkTransport : IDisposable
    {
        event Action<uint>? OnClientConnected;
        event Action<uint>? OnClientDisconnected;
        event Action<byte[], uint>? OnDataReceived;
        event Action? OnConnectedToServer;
        event Action? OnDisconnectedFromServer;

        bool IsHost { get; }
        bool IsClient { get; }
        bool IsConnected { get; }
        IReadOnlyDictionary<uint, INetworkClient> ConnectedClients { get; }

        bool StartServer(int port, int maxClients = 32);
        bool StartClient(IHostIdentifier serverAddress, int port);
        void Update();
        void SendToClient(uint clientId, byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable);
        void SendToAllClients(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable);
        void SendToServer(byte[] data, DeliveryMode deliveryMode = DeliveryMode.Reliable);
        void Stop();
    }
}