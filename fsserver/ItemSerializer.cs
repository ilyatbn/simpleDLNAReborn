using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using NMaier.SimpleDlna.Server;

namespace NMaier.SimpleDlna.FileMediaServer
{
  /// <summary>
  ///   Reads and writes the objects the <see cref="FileStore" /> cache holds.
  /// </summary>
  /// <remarks>
  ///   This used to be <c>BinaryFormatter</c>, which was removed from the
  ///   runtime in .NET 9. The cached types already expressed themselves through
  ///   <see cref="ISerializable" />, so rather than rewriting them this keeps
  ///   <see cref="SerializationInfo" /> as the interchange format and only
  ///   replaces the wire format underneath.
  ///
  ///   Unlike BinaryFormatter this never resolves an arbitrary type name off the
  ///   wire: the payload carries a short tag that must appear in
  ///   <see cref="Types" />, so a tampered cache database cannot instantiate
  ///   anything unexpected.
  /// </remarks>
  internal static class ItemSerializer
  {
    private const uint MAGIC = 0x53444C31; // "SDL1"

    /// <summary>
    ///   Tag to type. Tags are persisted, so never reuse one for another type;
    ///   bump FileStore.SCHEMA instead, which discards existing caches.
    /// </summary>
    private static readonly Dictionary<string, Type> Types =
      new Dictionary<string, Type>(StringComparer.Ordinal) {
        {"audio", typeof (AudioFile)},
        {"video", typeof (VideoFile)},
        {"image", typeof (ImageFile)},
        {"file", typeof (BaseFile)},
        {"cover", typeof (Cover)},
        {"subtitle", typeof (Subtitle)}
      };

    private static readonly Dictionary<Type, string> Tags = BuildTags();

    private static readonly FormatterConverter Converter =
      new FormatterConverter();

    /// <summary>Wire type of a single <see cref="SerializationInfo" /> entry.</summary>
    private enum Kind : byte
    {
      Null = 0,
      String = 1,
      Int32 = 2,
      Int64 = 3,
      Boolean = 4,
      Double = 5,
      ByteArray = 6,
      StringArray = 7,
      Object = 8
    }

    private static Dictionary<Type, string> BuildTags()
    {
      var rv = new Dictionary<Type, string>();
      foreach (var kv in Types) {
        rv.Add(kv.Value, kv.Key);
      }
      return rv;
    }

    public static void Serialize(Stream stream, object item)
    {
      if (stream == null) {
        throw new ArgumentNullException(nameof(stream));
      }
      if (item == null) {
        throw new ArgumentNullException(nameof(item));
      }
      // Leave the stream open: callers pull the bytes out afterwards.
      using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
        writer.Write(MAGIC);
        WriteObject(writer, item);
      }
    }

    public static object Deserialize(Stream stream, StreamingContext ctx)
    {
      if (stream == null) {
        throw new ArgumentNullException(nameof(stream));
      }
      using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) {
        if (reader.ReadUInt32() != MAGIC) {
          throw new SerializationException("Not a SimpleDLNA cache record");
        }
        return ReadObject(reader, ctx);
      }
    }

    public static bool CanSerialize(object item)
    {
      return item != null && Tags.ContainsKey(item.GetType());
    }

    private static void WriteObject(BinaryWriter writer, object item)
    {
      var type = item.GetType();
      string tag;
      if (!Tags.TryGetValue(type, out tag)) {
        throw new SerializationException($"{type} is not a cacheable type");
      }
      var serializable = item as ISerializable;
      if (serializable == null) {
        throw new SerializationException($"{type} is not ISerializable");
      }

      var info = new SerializationInfo(type, Converter);
      serializable.GetObjectData(
        info, new StreamingContext(StreamingContextStates.Persistence));

      // SerializationInfo has no count-then-enumerate contract that survives
      // being written lazily, so collect first.
      var entries = new List<SerializationEntry>();
      foreach (SerializationEntry entry in info) {
        entries.Add(entry);
      }

      writer.Write(tag);
      writer.Write(entries.Count);
      foreach (var entry in entries) {
        writer.Write(entry.Name);
        WriteValue(writer, entry.Value);
      }
    }

    private static object ReadObject(BinaryReader reader, StreamingContext ctx)
    {
      var tag = reader.ReadString();
      Type type;
      if (!Types.TryGetValue(tag, out type)) {
        throw new SerializationException($"Unknown cache record type {tag}");
      }

      var info = new SerializationInfo(type, Converter);
      var count = reader.ReadInt32();
      for (var i = 0; i < count; ++i) {
        var name = reader.ReadString();
        object value;
        var valueType = ReadValue(reader, ctx, out value);
        info.AddValue(name, value, valueType);
      }

      var ctor = type.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] {typeof (SerializationInfo), typeof (StreamingContext)},
        null);
      if (ctor == null) {
        throw new SerializationException(
          $"{type} has no deserialization constructor");
      }
      try {
        return ctor.Invoke(new object[] {info, ctx});
      }
      catch (TargetInvocationException ex) {
        throw new SerializationException(
          $"Failed to reconstruct {type}", ex.InnerException ?? ex);
      }
    }

    private static void WriteValue(BinaryWriter writer, object value)
    {
      if (value == null) {
        writer.Write((byte)Kind.Null);
        return;
      }

      var s = value as string;
      if (s != null) {
        writer.Write((byte)Kind.String);
        writer.Write(s);
        return;
      }
      if (value is int) {
        writer.Write((byte)Kind.Int32);
        writer.Write((int)value);
        return;
      }
      if (value is long) {
        writer.Write((byte)Kind.Int64);
        writer.Write((long)value);
        return;
      }
      if (value is bool) {
        writer.Write((byte)Kind.Boolean);
        writer.Write((bool)value);
        return;
      }
      if (value is double) {
        writer.Write((byte)Kind.Double);
        writer.Write((double)value);
        return;
      }
      var bytes = value as byte[];
      if (bytes != null) {
        writer.Write((byte)Kind.ByteArray);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        return;
      }
      var strings = value as string[];
      if (strings != null) {
        writer.Write((byte)Kind.StringArray);
        writer.Write(strings.Length);
        foreach (var i in strings) {
          // A null element is indistinguishable from an empty one here; none of
          // the cached arrays (actors, ...) carry nulls.
          writer.Write(i ?? string.Empty);
        }
        return;
      }

      writer.Write((byte)Kind.Object);
      WriteObject(writer, value);
    }

    private static Type ReadValue(BinaryReader reader, StreamingContext ctx,
      out object value)
    {
      var kind = (Kind)reader.ReadByte();
      switch (kind) {
      case Kind.Null:
        value = null;
        return typeof (object);
      case Kind.String:
        value = reader.ReadString();
        return typeof (string);
      case Kind.Int32:
        value = reader.ReadInt32();
        return typeof (int);
      case Kind.Int64:
        value = reader.ReadInt64();
        return typeof (long);
      case Kind.Boolean:
        value = reader.ReadBoolean();
        return typeof (bool);
      case Kind.Double:
        value = reader.ReadDouble();
        return typeof (double);
      case Kind.ByteArray: {
        var len = reader.ReadInt32();
        value = reader.ReadBytes(len);
        return typeof (byte[]);
      }
      case Kind.StringArray: {
        var len = reader.ReadInt32();
        var rv = new string[len];
        for (var i = 0; i < len; ++i) {
          rv[i] = reader.ReadString();
        }
        value = rv;
        return typeof (string[]);
      }
      case Kind.Object: {
        var rv = ReadObject(reader, ctx);
        value = rv;
        return rv.GetType();
      }
      default:
        throw new SerializationException($"Unknown value kind {kind}");
      }
    }
  }
}
