A WIP lightweight networking library with similar syntax to Unity's NGO for C# .NET that provides a simple network object registry and simple RPC functionality.

## Dependencies

- **Castle.Core** (≥5.0.0) - Method interception
- **ENet-CSharp** (≥2.4.8) - UDP networking
- **.NET 6.0+**

## Quick Start

### 1. Create a Networked Class
```csharp
public class Player : NetworkBehaviour
{
    [ClientRpc]
    public virtual void UpdateHealthClientRpc(float health) { }

    [ServerRpc(RequireOwnership = true)]
    public virtual void MovePlayerServerRpc(float x, float y) { }
}
```

### 2. Setup Network Manager
```csharp
var networkManager = new NetworkManager();

// Server
networkManager.StartServer(7777);

// Client  
networkManager.StartClient("127.0.0.1", 7777);

// Game loop
while (true)
{
    networkManager.Update();
    Thread.Sleep(16);
}
```

### 3. Use RPCs
```csharp
// Spawn object
var player = networkManager.SpawnNetworkObject<Player>();
player.OwnerClientId = clientId;

// Call RPCs - automatically routed over network
player.UpdateHealthClientRpc(100f);    // Server -> All clients
player.MovePlayerServerRpc(10f, 20f);  // Client -> Server
```

## RPC Attributes

**All RPC methods must be `virtual`**

### [ClientRpc] - Server calls, executes on all clients
```csharp
[ClientRpc]
public virtual void UpdateHealthClientRpc(float health) { }
```

### [ServerRpc] - Client calls, executes on server  
```csharp
[ServerRpc(RequireOwnership = true)]  // Default: requires ownership
public virtual void MovePlayerServerRpc(float x, float y) { }

[ServerRpc(RequireOwnership = false)] // Any client can call
public virtual void SendChatServerRpc(string message) { }
```

## TODO

- Add support for automatic network variables (sync fields across network)
- Remove requirement for RPC methods to be `virtual`
- Implement comprehensive unit and integration tests
- Add network performance monitoring and statistics tools
- Explore additional features for future releases