using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Handles spawning and synchronizing network objects across clients
    /// </summary>
    public static class NetworkObjectSpawner
    {
        private const byte SPAWN_OBJECT_MESSAGE = 255; // Special message type for spawning

        /// <summary>
        /// Serialize a network object spawn message
        /// </summary>
        public static byte[] SerializeSpawnObject(NetworkBehaviour networkObject)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Write message type
            writer.Write(SPAWN_OBJECT_MESSAGE);

            // Write object type name - get the base type if it's a proxy
            var objectType = networkObject.GetType();

            // If it's a Castle proxy, get the base type
            if (objectType.FullName?.StartsWith("Castle.Proxies.") == true)
            {
                objectType = objectType.BaseType ?? objectType;
            }

            var typeName = objectType.FullName ?? objectType.Name;
            writer.Write(typeName);

            // Write network object ID
            writer.Write(networkObject.NetworkObjectId);

            // Write owner client ID
            writer.Write(networkObject.OwnerClientId);

            return stream.ToArray();
        }

        /// <summary>
        /// Deserialize and handle a network object spawn message
        /// </summary>
        public static bool HandleSpawnMessage(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                using var reader = new BinaryReader(stream);

                // Read message type
                var messageType = reader.ReadByte();
                if (messageType != SPAWN_OBJECT_MESSAGE)
                    return false;

                // Read object type name
                var typeName = reader.ReadString();

                // Read network object ID
                var networkObjectId = reader.ReadUInt32();

                // Read owner client ID
                var ownerClientId = reader.ReadUInt32();

                // Try to create the object
                var objectType = Type.GetType(typeName);
                if (objectType == null)
                {
                    Console.WriteLine($"Unknown network object type: {typeName}");
                    return false;
                }

                // Use reflection to call NetworkBehaviour.CreateProxy<T>()
                var createProxyMethod = typeof(NetworkBehaviour).GetMethod("CreateProxy", BindingFlags.Public | BindingFlags.Static);
                if (createProxyMethod == null)
                {
                    Console.WriteLine("CreateProxy method not found");
                    return false;
                }

                var genericMethod = createProxyMethod.MakeGenericMethod(objectType);
                var networkObject = genericMethod.Invoke(null, null) as NetworkBehaviour;

                if (networkObject == null)
                {
                    Console.WriteLine($"Failed to create network object proxy of type: {typeName}");
                    return false;
                }

                // Set the properties from the network
                networkObject.SetNetworkProperties(networkObjectId, ownerClientId);

                Console.WriteLine($"Spawned network object: {typeName} (ID: {networkObjectId}, Owner: {ownerClientId})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling spawn message: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if data is a spawn message
        /// </summary>
        public static bool IsSpawnMessage(byte[] data)
        {
            return data.Length > 0 && data[0] == SPAWN_OBJECT_MESSAGE;
        }
    }
}