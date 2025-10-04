# Unity Netcode-like RPC System for ENet

This networking system provides a Unity Netcode for GameObjects-like experience using ENet for C#. It allows you to easily create networked objects with RPC (Remote Procedure Call) functionality using simple attributes.

## Features

- **[ClientRpc]** - Execute methods on all connected clients
- **[ServerRpc]** - Execute methods on the server
- **Ownership validation** - Control who can call specific RPCs
- **Multiple delivery modes** - Reliable, Unreliable, UnreliableSequenced
- **Automatic serialization** - Supports basic types, custom types via JSON
- **Easy setup** - Minimal boilerplate code

## Quick Start

### 1. Create a Networked Object

```csharp
using RogueLike.Netcode;

public class MyNetworkedObject : NetworkBehaviour
{
    public float Health { get; private set; } = 100f;

    // This method runs on all clients when called from the server
    [ClientRpc]
    public void TakeDamageClientRpc(float damage)
    {
        Health -= damage;
        Console.WriteLine($"Took {damage} damage. Health: {Health}");
    }

    // This method runs on the server when called from a client
    [ServerRpc(RequireOwnership = true)]
    public void RequestHealServerRpc(float amount)
    {
        Health += amount;
        // Notify all clients of the health change
        UpdateHealthClientRpc(Health);
    }

    [ClientRpc]
    public void UpdateHealthClientRpc(float newHealth)
    {
        Health = newHealth;
    }
}
```

### 2. Setup Network Manager

```csharp
// Server
var networkManager = new NetworkManager();
networkManager.StartServer(7777); // Start server on port 7777

// Client
var networkManager = new NetworkManager();
networkManager.StartClient("127.0.0.1", 7777); // Connect to server

// Update in your game loop
networkManager.Update();
```

### 3. Use Your Networked Objects

```csharp
var myObject = new MyNetworkedObject();
myObject.OwnerClientId = clientId; // Set ownership

// On server: Call ClientRpc to update all clients
myObject.TakeDamageClientRpc(25f);

// On client: Call ServerRpc to request server action
myObject.RequestHealServerRpc(10f);
```

## RPC Attributes

### [ClientRpc]
- **Purpose**: Execute method on all connected clients
- **Called from**: Server only
- **Options**:
  - `RequireOwnership = false` (default): Anyone can call
  - `RequireOwnership = true`: Only the owner can call
  - `DeliveryMode = DeliveryMode.Reliable` (default)

```csharp
[ClientRpc(RequireOwnership = true, DeliveryMode = DeliveryMode.Unreliable)]
public void UpdatePositionClientRpc(float x, float y)
{
    // This runs on all clients
}
```

### [ServerRpc]
- **Purpose**: Execute method on the server
- **Called from**: Clients only
- **Options**:
  - `RequireOwnership = true` (default): Only the owner can call
  - `RequireOwnership = false`: Anyone can call
  - `DeliveryMode = DeliveryMode.Reliable` (default)

```csharp
[ServerRpc(RequireOwnership = false)]
public void SendChatMessageServerRpc(string message)
{
    // This runs on the server
}
```

## Delivery Modes

- **Reliable**: Guaranteed delivery, ordered
- **Unreliable**: Fast, no delivery guarantee
- **UnreliableSequenced**: Fast, drops old packets

## Supported Parameter Types

### Built-in Types
- `bool`, `byte`, `sbyte`
- `short`, `ushort`, `int`, `uint`, `long`, `ulong`
- `float`, `double`
- `string`
- `Vector2`, `Vector3` (included utility structs)

### Custom Types
Any type that can be serialized to JSON is automatically supported.

## Best Practices

### 1. Ownership Management
```csharp
// Set ownership when creating objects
var player = new NetworkedPlayer();
player.OwnerClientId = networkManager.LocalClientId;
```

### 2. Validation on Server
```csharp
[ServerRpc]
public void MovePlayerServerRpc(float x, float y)
{
    // Always validate input on server
    if (IsValidPosition(x, y))
    {
        UpdatePositionClientRpc(x, y);
    }
}
```

### 3. Use Appropriate Delivery Modes
```csharp
// For critical data (health, score)
[ClientRpc(DeliveryMode = DeliveryMode.Reliable)]
public void UpdateHealthClientRpc(float health) { }

// For frequent updates (position, animation)
[ClientRpc(DeliveryMode = DeliveryMode.Unreliable)]
public void UpdatePositionClientRpc(float x, float y) { }
```

### 4. Minimize RPC Calls
```csharp
// Bad: Multiple RPCs
UpdateHealthClientRpc(health);
UpdateManaClientRpc(mana);
UpdateStaminaClientRpc(stamina);

// Good: Single RPC with multiple values
UpdateStatsClientRpc(health, mana, stamina);
```

## Example Integration

See `Netcode/Examples/NetworkExample.cs` for a complete working example showing:
- Server setup
- Client connection
- Player movement
- Combat system
- Name changes
- Proper cleanup

## Architecture

```
NetworkManager
├── NetworkTransport (ENet wrapper)
├── NetworkBehaviour (Base class for networked objects)
├── RpcSerializer (Handles message serialization)
└── NetworkAttributes (RPC attributes)
```

## Error Handling

The system includes built-in error handling for:
- Invalid RPC calls
- Ownership violations
- Serialization failures
- Network disconnections

Errors are logged to console and don't crash the application.

## Limitations

1. No automatic state synchronization (you must manually sync state via RPCs)
2. No built-in interpolation/extrapolation
3. Basic ownership model (single owner per object)
4. No automatic object spawning/despawning over network

## Future Enhancements

- NetworkVariable system for automatic state sync
- Object spawning/despawning
- Client prediction and lag compensation
- More sophisticated ownership models
- Built-in interpolation system