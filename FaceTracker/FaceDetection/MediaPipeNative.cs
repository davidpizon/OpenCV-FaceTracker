using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Protobuf;
using Mediapipe.Net.Native;
using System.Runtime.InteropServices;

namespace FaceFinderDemo.FaceDetection;

[StructLayout(LayoutKind.Sequential)]
internal struct MpSerializedProto
{
    public IntPtr StrPtr;
    public int Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpSerializedProtoVector
{
    public IntPtr Data;
    public int Size;
}

internal static class MediaPipeNativeMethods
{
    [DllImport("mediapipe_c")]
    public static extern MpReturnCode mp_Packet__GetNormalizedLandmarkListVector(
        IntPtr packet, out MpSerializedProtoVector value);

    [DllImport("mediapipe_c")]
    public static extern void mp_api_SerializedProtoArray__delete(IntPtr data, int size);
}

public class NormalizedLandmarkListVectorPacket : Packet<List<NormalizedLandmarkList>>
{
    public NormalizedLandmarkListVectorPacket() : base(isOwner: true) { }

    public NormalizedLandmarkListVectorPacket(IntPtr ptr, bool isOwner = true) : base(ptr, isOwner) { }

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
