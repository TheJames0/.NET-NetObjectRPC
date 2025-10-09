using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text.Json;

namespace RogueLike.Netcode
{
    /// <summary>
    /// Handles serialization and deserialization of RPC calls
    /// </summary>
    public static class RpcSerializer
    {
        private static readonly Dictionary<Type, byte> TypeToId = new()
        {
            { typeof(bool), 1 },
            { typeof(byte), 2 },
            { typeof(sbyte), 3 },
            { typeof(short), 4 },
            { typeof(ushort), 5 },
            { typeof(int), 6 },
            { typeof(uint), 7 },
            { typeof(long), 8 },
            { typeof(ulong), 9 },
            { typeof(float), 10 },
            { typeof(double), 11 },
            { typeof(string), 12 },
            { typeof(System.Numerics.Vector2), 13 },
            { typeof(System.Numerics.Vector3), 14 }
        };

        private static readonly Dictionary<byte, Type> IdToType = new();

        static RpcSerializer()
        {
            foreach (var kvp in TypeToId)
            {
                IdToType[kvp.Value] = kvp.Key;
            }
        }

        /// <summary>
        /// Serialize an RPC call to bytes
        /// </summary>
        public static byte[] SerializeRpcCall(string methodName, uint networkObjectId, object[] parameters)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(methodName);
            writer.Write(networkObjectId);
            writer.Write(parameters?.Length ?? 0);

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    SerializeParameter(writer, param);
                }
            }

            return stream.ToArray();
        }

        /// <summary>
        /// Deserialize bytes back to an RPC call
        /// </summary>
        public static (string methodName, uint networkObjectId, object[] parameters) DeserializeRpcCall(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);

            var methodName = reader.ReadString();
            var networkObjectId = reader.ReadUInt32();
            var paramCount = reader.ReadInt32();

            var parameters = new object[paramCount];
            for (int i = 0; i < paramCount; i++)
            {
                parameters[i] = DeserializeParameter(reader);
            }

            return (methodName, networkObjectId, parameters);
        }

        /// <summary>
        /// Serialize a single parameter to binary
        /// </summary>
        private static void SerializeParameter(BinaryWriter writer, object parameter)
        {
            if (parameter == null)
            {
                writer.Write((byte)0);
                return;
            }

            var type = parameter.GetType();

            if (TypeToId.TryGetValue(type, out byte typeId))
            {
                writer.Write(typeId);
                WriteValue(writer, parameter, type);
            }
            else
            {
                writer.Write((byte)255);
                writer.Write(type.AssemblyQualifiedName!);
                var json = JsonSerializer.Serialize(parameter);
                writer.Write(json);
            }
        }

        /// <summary>
        /// Deserialize a single parameter from binary
        /// </summary>
        private static object DeserializeParameter(BinaryReader reader)
        {
            var typeId = reader.ReadByte();

            if (typeId == 0)
                return null!;

            if (typeId == 255)
            {
                var typeName = reader.ReadString();
                var json = reader.ReadString();
                var deserializeType = Type.GetType(typeName);
                if (deserializeType == null)
                    throw new InvalidOperationException($"Could not find type: {typeName}");
                return JsonSerializer.Deserialize(json, deserializeType) ?? throw new InvalidOperationException("Deserialization returned null");
            }

            if (IdToType.TryGetValue(typeId, out var type))
            {
                return ReadValue(reader, type);
            }

            throw new InvalidOperationException($"Unknown type ID: {typeId}");
        }

        /// <summary>
        /// Write a value of a specific type to binary
        /// </summary>
        private static void WriteValue(BinaryWriter writer, object value, Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    writer.Write((bool)value);
                    break;
                case TypeCode.Byte:
                    writer.Write((byte)value);
                    break;
                case TypeCode.SByte:
                    writer.Write((sbyte)value);
                    break;
                case TypeCode.Int16:
                    writer.Write((short)value);
                    break;
                case TypeCode.UInt16:
                    writer.Write((ushort)value);
                    break;
                case TypeCode.Int32:
                    writer.Write((int)value);
                    break;
                case TypeCode.UInt32:
                    writer.Write((uint)value);
                    break;
                case TypeCode.Int64:
                    writer.Write((long)value);
                    break;
                case TypeCode.UInt64:
                    writer.Write((ulong)value);
                    break;
                case TypeCode.Single:
                    writer.Write((float)value);
                    break;
                case TypeCode.Double:
                    writer.Write((double)value);
                    break;
                case TypeCode.String:
                    writer.Write((string)value);
                    break;
                default:
                    if (type == typeof(System.Numerics.Vector2))
                    {
                        var v = (System.Numerics.Vector2)value;
                        writer.Write(v.X);
                        writer.Write(v.Y);
                    }
                    else if (type == typeof(System.Numerics.Vector3))
                    {
                        var v = (System.Numerics.Vector3)value;
                        writer.Write(v.X);
                        writer.Write(v.Y);
                        writer.Write(v.Z);
                    }
                    break;
            }
        }

        private static object ReadValue(BinaryReader reader, Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return reader.ReadBoolean();
                case TypeCode.Byte:
                    return reader.ReadByte();
                case TypeCode.SByte:
                    return reader.ReadSByte();
                case TypeCode.Int16:
                    return reader.ReadInt16();
                case TypeCode.UInt16:
                    return reader.ReadUInt16();
                case TypeCode.Int32:
                    return reader.ReadInt32();
                case TypeCode.UInt32:
                    return reader.ReadUInt32();
                case TypeCode.Int64:
                    return reader.ReadInt64();
                case TypeCode.UInt64:
                    return reader.ReadUInt64();
                case TypeCode.Single:
                    return reader.ReadSingle();
                case TypeCode.Double:
                    return reader.ReadDouble();
                case TypeCode.String:
                    return reader.ReadString();
                default:
                    if (type == typeof(System.Numerics.Vector2))
                    {
                        return new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
                    }
                    else if (type == typeof(System.Numerics.Vector3))
                    {
                        return new System.Numerics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }
                    break;
            }

            throw new InvalidOperationException($"Cannot deserialize type: {type}");
        }
    }

    // Simple Vector types for serialization
    public struct Vector2
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public struct Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}