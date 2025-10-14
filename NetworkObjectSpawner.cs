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
        private const byte SPAWN_OBJECT_MESSAGE = 255;

        /// <summary>
        /// Serialize a network object spawn message
        /// </summary>
        public static byte[] SerializeSpawnObject(NetworkBehaviour networkObject)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(SPAWN_OBJECT_MESSAGE);

            var objectType = networkObject.GetType();

            if (objectType.FullName?.StartsWith("Castle.Proxies.") == true)
            {
                objectType = objectType.BaseType ?? objectType;
            }

            var typeName = objectType.FullName ?? objectType.Name;
            writer.Write(typeName);

            writer.Write(networkObject.NetworkObjectId);
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

                var messageType = reader.ReadByte();
                if (messageType != SPAWN_OBJECT_MESSAGE)
                    return false;

                var typeName = reader.ReadString();
                var networkObjectId = reader.ReadUInt32();
                var ownerClientId = reader.ReadUInt32();

                // Check if object already exists
                if (NetworkBehaviour.GetNetworkObject(networkObjectId) != null)
                    return true;

                var objectType = Type.GetType(typeName);
                if (objectType == null)
                {
                    Console.WriteLine($"Unknown network object type: {typeName}");
                    return false;
                }

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

                networkObject.SetNetworkProperties(networkObjectId, ownerClientId);
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