using FaceFinderDemo.ImageProcessing;
using Mediapipe.Net.Framework;
using Mediapipe.Net.Framework.Format;
using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Port;
using Mediapipe.Net.Framework.Protobuf;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace FaceFinderDemo.FaceDetection;

/// <summary>
/// Image-processing pipeline processor that runs the MediaPipe Face Mesh graph to detect
/// 468 (or 478 with irises) face landmarks and draws a mesh overlay onto each frame.
/// </summary>
/// <remarks>
/// Call <see cref="InitializeAsync"/> once before connecting an upstream source. Until
/// initialisation completes, received frames are forwarded unchanged. The MediaPipe
/// <c>CalculatorGraph</c> runs synchronously inside <see cref="OnImageReceived"/> — each
/// frame is fed to the graph and the method blocks until the graph is idle before the
/// annotated frame is emitted downstream.
/// <para>
/// The native landmark callback is pinned with <see cref="GCHandleType.Normal"/> rather
/// than <see cref="GCHandleType.Pinned"/> because delegates contain managed references
/// and cannot be pinned on .NET 9+.
/// </para>
/// </remarks>
public class FaceMeshDevice : ImageProcessor, IDisposable
{
    // Pre-computed unique boundary-point index arrays for iris drawing — built once from static connection data.
    private static readonly int[] _leftIrisBoundaryIndices = GetUniqueBoundaryIndices(FaceMeshConnections.LeftIris);
    private static readonly int[] _rightIrisBoundaryIndices = GetUniqueBoundaryIndices(FaceMeshConnections.RightIris);

    private static int[] GetUniqueBoundaryIndices((int, int)[] connections)
    {
        var set = new HashSet<int>();
        foreach (var (a, b) in connections) { set.Add(a); set.Add(b); }
        return [.. set];
    }

    private const string GraphConfig = @"
input_stream: ""input_video""
output_stream: ""multi_face_landmarks""
output_stream: ""face_rects_from_landmarks""
output_stream: ""face_detections""
output_stream: ""face_rects_from_detections""
node {
  calculator: ""ConstantSidePacketCalculator""
  output_side_packet: ""PACKET:num_faces""
  node_options: {
    [type.googleapis.com/mediapipe.ConstantSidePacketCalculatorOptions]: {
      packet { int_value: 1 }
    }
  }
}
node {
  calculator: ""ConstantSidePacketCalculator""
  output_side_packet: ""PACKET:with_attention""
  node_options: {
    [type.googleapis.com/mediapipe.ConstantSidePacketCalculatorOptions]: {
      packet { bool_value: true }
    }
  }
}
node {
  calculator: ""FaceLandmarkFrontCpu""
  input_stream: ""IMAGE:input_video""
  input_side_packet: ""NUM_FACES:num_faces""
  input_side_packet: ""WITH_ATTENTION:with_attention""
  output_stream: ""LANDMARKS:multi_face_landmarks""
  output_stream: ""ROIS_FROM_LANDMARKS:face_rects_from_landmarks""
  output_stream: ""DETECTIONS:face_detections""
  output_stream: ""ROIS_FROM_DETECTIONS:face_rects_from_detections""
}
";

    private CalculatorGraph? _graph;
    private GCHandle _callbackHandle;
    private Mat? _currentFrame;
    private readonly object _frameLock = new();
    private long _frameTimestamp;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Optional callback invoked with status messages during model download and initialisation.
    /// Set this before calling <see cref="InitializeAsync"/>.
    /// </summary>
    public Action<string>? OnStatus;

    /// <summary>
    /// <see langword="true"/> once <see cref="InitializeAsync"/> has completed successfully
    /// and frames can be processed.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// When <see langword="true"/> (default), the face mesh follows the face position in the
    /// frame. When <see langword="false"/>, the mesh is drawn centred on the frame with head
    /// orientation removed.
    /// </summary>
    public bool TrackHead { get; set; } = true;

    /// <summary>
    /// Downloads any missing TFLite models, constructs the <c>CalculatorGraph</c>, registers
    /// the landmark output callback, and starts the graph. Safe to call multiple times;
    /// subsequent calls return immediately if already initialised.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var resourceManager = new MediaPipeResourceManager();
        await resourceManager.EnsureModelsDownloadedAsync(OnStatus);

        _graph = new CalculatorGraph(GraphConfig);

        CalculatorGraph.NativePacketCallback nativeCallback = (graphPtr, streamId, packetPtr) =>
        {
            var packet = new NormalizedLandmarkListVectorPacket(packetPtr, isOwner: false);
            OnLandmarks(packet);
            return Status.StatusArgs.Ok();
        };
        _callbackHandle = GCHandle.Alloc(nativeCallback, GCHandleType.Normal);
        _graph.ObserveOutputStream("multi_face_landmarks", 0, nativeCallback, false).AssertOk();

        _graph.StartRun().AssertOk();
        _initialized = true;
    }

    protected override void OnImageReceived(Mat image)
    {
        if (!_initialized || _graph == null || _disposed) return;

        lock (_frameLock)
            _currentFrame = image;

        using var rgbMat = new Mat();
        Cv2.CvtColor(image, rgbMat, ColorConversionCodes.BGR2RGB);

        var width = rgbMat.Width;
        var height = rgbMat.Height;
        var widthStep = (int)rgbMat.Step();
        int dataSize = widthStep * height;

        unsafe
        {
            using var imageFrame = new ImageFrame(
                ImageFormat.Types.Format.Srgb,
                width, height, widthStep,
                new ReadOnlySpan<byte>(rgbMat.Data.ToPointer(), dataSize));
                using var packet = new ImageFramePacket(imageFrame, new Timestamp(_frameTimestamp++));

                _graph.AddPacketToInputStream("input_video", packet).AssertOk();
                _graph.WaitUntilIdle().AssertOk();
            }

            OnImageAvailable(image);
        }

    /// <summary>
    /// Native packet callback invoked by the MediaPipe graph when landmark results are ready.
    /// Reads the current frame under <see cref="_frameLock"/> and calls <see cref="DrawFaceMesh"/>
    /// for each detected face.
    /// </summary>
    private void OnLandmarks(NormalizedLandmarkListVectorPacket packet)
    {
        Mat? frame;
        lock (_frameLock)
            frame = _currentFrame;
        if (frame == null) return;

        var faces = packet.Get();
        foreach (var face in faces)
            DrawFaceMesh(frame, face);
    }

    /// <summary>
    /// Draws the full face mesh topology for a single face onto <paramref name="frame"/>.
    /// Uses depth (Z coordinate) to modulate the brightness of tessellation lines.
    /// Draws irises as circles only when 478 landmarks are present (attention model).
    /// </summary>
    private void DrawFaceMesh(Mat frame, NormalizedLandmarkList face)
    {
        var landmarks = face.Landmark;
        int count = landmarks.Count;
        if (count == 0) return;

        int w = frame.Width;
        int h = frame.Height;

        // Centroid — always computed; used for de-rotation when head tracking is off.
        double sumX = 0, sumY = 0, sumZ = 0;
        foreach (var lm in landmarks) { sumX += lm.X; sumY += lm.Y; sumZ += lm.Z; }
        double centX = sumX / count, centY = sumY / count, centZ = sumZ / count;

        // 3-D orientation axes — only computed when head tracking is disabled.
        double rX0 = 1, rX1 = 0, rX2 = 0;
        double rY0 = 0, rY1 = 1, rY2 = 0;

        // Fill-scale and centroid of de-rotated landmarks — set inside the !TrackHead block.
        double fillScale = 1.0, midNx = 0.0, midNy = 0.0;

        if (!TrackHead)
        {
            int liIdx = Math.Min(33, count - 1), riIdx = Math.Min(263, count - 1);
            double eyeX = landmarks[riIdx].X - landmarks[liIdx].X;
            double eyeY = landmarks[riIdx].Y - landmarks[liIdx].Y;
            double eZ   = landmarks[riIdx].Z - landmarks[liIdx].Z;
            double e3DLen = Math.Sqrt(eyeX * eyeX + eyeY * eyeY + eZ * eZ);
            if (e3DLen > 1e-6) { rX0 = eyeX / e3DLen; rX1 = eyeY / e3DLen; rX2 = eZ / e3DLen; }

            int foreheadIdx = Math.Min(10, count - 1), chinIdx = Math.Min(152, count - 1);
            double dX = landmarks[chinIdx].X - landmarks[foreheadIdx].X;
            double dY = landmarks[chinIdx].Y - landmarks[foreheadIdx].Y;
            double dZ = landmarks[chinIdx].Z - landmarks[foreheadIdx].Z;
            double dLen = Math.Sqrt(dX * dX + dY * dY + dZ * dZ);
            if (dLen > 1e-6) { dX /= dLen; dY /= dLen; dZ /= dLen; }

            // Face-forward (outward normal): cross(downEstimate, faceRight)
            double fX = dY * rX2 - dZ * rX1;
            double fY = dZ * rX0 - dX * rX2;
            double fZ = dX * rX1 - dY * rX0;
            double fLen = Math.Sqrt(fX * fX + fY * fY + fZ * fZ);
            if (fLen > 1e-6) { fX /= fLen; fY /= fLen; fZ /= fLen; }

            // Face-down (orthogonalised): cross(faceRight, faceForward)
            rY0 = rX1 * fZ - rX2 * fY;
            rY1 = rX2 * fX - rX0 * fZ;
            rY2 = rX0 * fY - rX1 * fX;
            double yLen = Math.Sqrt(rY0 * rY0 + rY1 * rY1 + rY2 * rY2);
            if (yLen > 1e-6) { rY0 /= yLen; rY1 /= yLen; rY2 /= yLen; }

            // First pass: bounding box of all de-rotated landmarks.
            double minNx = double.MaxValue, maxNx = double.MinValue;
            double minNy = double.MaxValue, maxNy = double.MinValue;
            foreach (var lm in landmarks)
            {
                double dx = lm.X - centX, dy = lm.Y - centY, dz = lm.Z - centZ;
                double nx = dx * rX0 + dy * rX1 + dz * rX2;
                double ny = dx * rY0 + dy * rY1 + dz * rY2;
                if (nx < minNx) minNx = nx;
                if (nx > maxNx) maxNx = nx;
                if (ny < minNy) minNy = ny;
                if (ny > maxNy) maxNy = ny;
            }

            // Uniform scale so the largest span fills the frame with a 5% margin on each side.
            double spanX = maxNx - minNx;
            double spanY = maxNy - minNy;
            if (spanX > 1e-6 && spanY > 1e-6)
                fillScale = Math.Min(0.9 / spanX, 0.9 / spanY);
            midNx = (minNx + maxNx) * 0.5;
            midNy = (minNy + maxNy) * 0.5;
        }

        Point LandmarkToPoint(int idx)
        {
            if (idx >= count) return new Point(0, 0);
            var lm = landmarks[idx];
            if (!TrackHead)
            {
                double dx = lm.X - centX, dy = lm.Y - centY, dz = lm.Z - centZ;
                double nx = dx * rX0 + dy * rX1 + dz * rX2;
                double ny = dx * rY0 + dy * rY1 + dz * rY2;
                return new Point(
                    (int)(((nx - midNx) * fillScale + 0.5) * w),
                    (int)(((ny - midNy) * fillScale + 0.5) * h));
            }
            return new Point((int)(lm.X * w), (int)(lm.Y * h));
        }

        Scalar DepthColor(int idx, double brightness)
        {
            if (idx >= count) return new Scalar(0, brightness, 0);
            var z = landmarks[idx].Z;
            var depth = Math.Clamp((z + 0.15f) / 0.30f, 0f, 1f);
            var v = (int)(brightness * (0.3 + 0.7 * depth));
            return new Scalar(0, v, 0);
        }

        // Tessellation — dim depth-coloured green
        foreach (var (a, b) in FaceMeshConnections.Tesselation)
        {
            if (a >= count || b >= count) continue;
            var color = DepthColor(a, 80);
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), color, 1, LineTypes.AntiAlias);
        }

        // Face oval — bright white
        foreach (var (a, b) in FaceMeshConnections.FaceOval)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(220, 220, 220), 1, LineTypes.AntiAlias);
        }

        // Lips — red
        foreach (var (a, b) in FaceMeshConnections.Lips)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(80, 80, 220), 1, LineTypes.AntiAlias);
        }

        // Eyes — cyan
        foreach (var (a, b) in FaceMeshConnections.LeftEye)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(220, 220, 0), 1, LineTypes.AntiAlias);
        }
        foreach (var (a, b) in FaceMeshConnections.RightEye)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(220, 220, 0), 1, LineTypes.AntiAlias);
        }

        // Eyebrows — yellow
        foreach (var (a, b) in FaceMeshConnections.LeftEyebrow)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(0, 220, 220), 1, LineTypes.AntiAlias);
        }
        foreach (var (a, b) in FaceMeshConnections.RightEyebrow)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(0, 220, 220), 1, LineTypes.AntiAlias);
        }

        // Nose — light gray
        foreach (var (a, b) in FaceMeshConnections.Nose)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(200, 200, 200), 1, LineTypes.AntiAlias);
        }

        // Iris — only if 478 landmarks present (with_attention model)
        if (count >= 478)
        {
            DrawIris(frame,
                FaceMeshConnections.IrisCenterLeft,
                _leftIrisBoundaryIndices,
                new Scalar(0, 255, 255), LandmarkToPoint);

            DrawIris(frame,
                FaceMeshConnections.IrisCenterRight,
                _rightIrisBoundaryIndices,
                new Scalar(0, 255, 255), LandmarkToPoint);
        }
    }

    /// <summary>
    /// Draws a circle representing a single iris by estimating its radius from the average
    /// distance between the iris centre landmark and its surrounding boundary points.
    /// </summary>
    /// <param name="frame">The frame to draw onto.</param>
    /// <param name="landmarks">All face landmarks for the current face.</param>
    /// <param name="w">Frame width in pixels.</param>
    /// <param name="h">Frame height in pixels.</param>
    /// <param name="centerIdx">Landmark index of the iris centre point.</param>
    /// <param name="boundaryConnections">Landmark index pairs forming the iris boundary ring.</param>
    /// <param name="color">BGR color for the iris circle.</param>
    private static void DrawIris(Mat frame,
        int centerIdx, int[] boundaryIndices, Scalar color,
        Func<int, Point> landmarkToPoint)
    {
        var cp = landmarkToPoint(centerIdx);

        double totalDist = 0;
        int distCount = 0;
        foreach (var idx in boundaryIndices)
        {
            var p = landmarkToPoint(idx);
            var dx = p.X - cp.X;
            var dy = p.Y - cp.Y;
            totalDist += Math.Sqrt(dx * dx + dy * dy);
            distCount++;
        }

        int radius = distCount > 0 ? Math.Max(1, (int)(totalDist / distCount)) : 3;

        Cv2.Circle(frame, cp, radius, color, 1, LineTypes.AntiAlias);
        Cv2.Circle(frame, cp, 1, color, -1, LineTypes.AntiAlias);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_graph != null)
        {
            try
            {
                _graph.CloseAllPacketSources().AssertOk();
                _graph.WaitUntilDone().AssertOk();
            }
            catch { }

            if (_callbackHandle.IsAllocated)
                _callbackHandle.Free();

            _graph.Dispose();
            _graph = null;
        }
    }
}
