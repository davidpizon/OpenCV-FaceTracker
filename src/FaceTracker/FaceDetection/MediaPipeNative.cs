using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Protobuf;
using Mediapipe.Net.Native;
using System.Runtime.InteropServices;

namespace FaceFinderDemo.FaceDetection;

/// <summary>
/// Blittable representation of a single serialized protobuf message as returned
/// by the native MediaPipe C API.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpSerializedProto
{
    /// <summary>Pointer to the raw protobuf bytes allocated by native code.</summary>
    public IntPtr StrPtr;

    /// <summary>Length of the byte array at <see cref="StrPtr"/>.</summary>
    public int Length;
}

/// <summary>
/// Blittable representation of a native array of <see cref="MpSerializedProto"/> elements,
/// as returned by <c>mp_Packet__GetNormalizedLandmarkListVector</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpSerializedProtoVector
{
    /// <summary>Pointer to the first <see cref="MpSerializedProto"/> element.</summary>
    public IntPtr Data;

    /// <summary>Number of elements in the array.</summary>
    public int Size;
}

/// <summary>
/// P/Invoke declarations for MediaPipe C API functions not exposed by Mediapipe.Net.
/// </summary>
internal static class MediaPipeNativeMethods
{
    /// <summary>
    /// Extracts a vector of serialized <c>NormalizedLandmarkList</c> protos from a packet.
    /// </summary>
    [DllImport("mediapipe_c")]
    public static extern MpReturnCode mp_Packet__GetNormalizedLandmarkListVector(
        IntPtr packet, out MpSerializedProtoVector value);

    /// <summary>
    /// Frees the native array of serialized proto elements returned by
    /// <see cref="mp_Packet__GetNormalizedLandmarkListVector"/>.
    /// Must be called after the data has been consumed to avoid a native memory leak.
    /// </summary>
    [DllImport("mediapipe_c")]
    public static extern void mp_api_SerializedProtoArray__delete(IntPtr data, int size);
}

/// <summary>
/// A <see cref="Packet{T}"/> that deserializes a native
/// <c>std::vector&lt;NormalizedLandmarkList&gt;</c> packet produced by the face-mesh graph.
/// </summary>
/// <remarks>
/// <see cref="Get"/> uses unsafe pointer arithmetic to walk the
/// <see cref="MpSerializedProtoVector"/> array, parses each element as a protobuf message,
/// and frees the native allocation before returning.
/// </remarks>
public class NormalizedLandmarkListVectorPacket : Packet<List<NormalizedLandmarkList>>
{
    /// <summary>Creates a new owning packet (used by Mediapipe.Net infrastructure).</summary>
    public NormalizedLandmarkListVectorPacket() : base(isOwner: true) { }

    /// <summary>Wraps an existing native packet pointer.</summary>
    /// <param name="ptr">Native packet handle.</param>
    /// <param name="isOwner">Whether this instance should free the pointer on disposal.</param>
    public NormalizedLandmarkListVectorPacket(IntPtr ptr, bool isOwner = true) : base(ptr, isOwner) { }

    /// <summary>
    /// Deserializes the native landmark vector into a managed list.
    /// Walks the <see cref="MpSerializedProtoVector"/> via unsafe pointer arithmetic,
    /// parses each entry as a <c>NormalizedLandmarkList</c> proto, and frees the
    /// native array before returning.
    /// </summary>
    /// <returns>A list containing one <c>NormalizedLandmarkList</c> per detected face.</returns>
    public override List<NormalizedLandmarkList> Get()
    {
        MediaPipeNativeMethods.mp_Packet__GetNormalizedLandmarkListVector(MpPtr, out var vec)
            .Assert();
        GC.KeepAlive(this);

        var result = new List<NormalizedLandmarkList>(vec.Size);
        unsafe
        {
            var protoSize = Marshal.SizeOf<MpSerializedProto>();
            var ptr = (byte*)vec.Data.ToPointer();
            for (int i = 0; i < vec.Size; i++)
            {
                var proto = Marshal.PtrToStructure<MpSerializedProto>((IntPtr)ptr);
                ptr += protoSize;

                var bytes = new byte[proto.Length];
                Marshal.Copy(proto.StrPtr, bytes, 0, proto.Length);
                result.Add(NormalizedLandmarkList.Parser.ParseFrom(bytes));
            }
        }

        MediaPipeNativeMethods.mp_api_SerializedProtoArray__delete(vec.Data, vec.Size);
        return result;
    }

    public override Mediapipe.Net.Framework.Port.StatusOr<List<NormalizedLandmarkList>> Consume() =>
        throw new NotSupportedException();

    public override Mediapipe.Net.Framework.Port.Status ValidateAsType() =>
        throw new NotSupportedException();
}
