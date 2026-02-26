using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FaceFinderDemo.ImageProcessing;
using Mediapipe.Net.Framework;
using Mediapipe.Net.Framework.Format;
using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Port;
using Mediapipe.Net.Framework.Protobuf;
using OpenCvSharp;

namespace FaceFinderDemo.FaceDetection;

public class FaceMeshDevice : ImageProcessor, IDisposable
{
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
    private OutputStreamPoller<List<NormalizedLandmarkList>>? _poller;
    private long _frameTimestamp;
    private bool _initialized;
    private bool _disposed;
    private readonly object _graphLock = new();

    public Action<string>? OnStatus;

    public bool IsInitialized => _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var resourceManager = new MediaPipeResourceManager();
        await resourceManager.EnsureModelsDownloadedAsync(OnStatus);

        _graph = new CalculatorGraph(GraphConfig);

        _poller = _graph.AddOutputStreamPoller<List<NormalizedLandmarkList>>("multi_face_landmarks").Value();

        _graph.StartRun().AssertOk();
        _initialized = true;
    }

    protected override void OnImageReceived(Mat image)
    {
        if (!_initialized || _graph == null || _disposed) return;

        lock (_graphLock)
        {
            if (!_initialized || _graph == null || _disposed) return;

            using var rgbMat = new Mat();
            Cv2.CvtColor(image, rgbMat, ColorConversionCodes.BGR2RGB);

            var width = rgbMat.Width;
            var height = rgbMat.Height;
            var widthStep = (int)rgbMat.Step();
            int dataSize = widthStep * height;

            var pixelData = new byte[dataSize];
            Marshal.Copy(rgbMat.Data, pixelData, 0, dataSize);

            using var imageFrame = new ImageFrame(
                ImageFormat.Types.Format.Srgb,
                width, height, widthStep,
                new ReadOnlySpan<byte>(pixelData));
            using var packet = new ImageFramePacket(imageFrame, new Timestamp(_frameTimestamp++));

            _graph.AddPacketToInputStream("input_video", packet).AssertOk();
            _graph.WaitUntilIdle().AssertOk();

            using var pkt = new NormalizedLandmarkListVectorPacket();
            if (_poller!.Next(pkt) && !pkt.IsEmpty())
            {
                var faces = pkt.Get();
                foreach (var face in faces)
                    DrawFaceMesh(image, face);
            }

            OnImageAvailable(image);
        }
    }

    private static void DrawFaceMesh(Mat frame, NormalizedLandmarkList face)
    {
        var landmarks = face.Landmark;
        int count = landmarks.Count;
        if (count == 0) return;

        int w = frame.Width;
        int h = frame.Height;

        Point LandmarkToPoint(int idx)
        {
            if (idx >= count) return new Point(0, 0);
            var lm = landmarks[idx];
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

        // Nose — pale cyan
        foreach (var (a, b) in FaceMeshConnections.Nose)
        {
            if (a >= count || b >= count) continue;
            Cv2.Line(frame, LandmarkToPoint(a), LandmarkToPoint(b), new Scalar(200, 200, 200), 1, LineTypes.AntiAlias);
        }

        // Iris — only if 478 landmarks present (with_attention model)
        if (count >= 478)
        {
            DrawIris(frame, landmarks, w, h,
                FaceMeshConnections.IrisCenterLeft,
                FaceMeshConnections.LeftIris,
                new Scalar(0, 255, 255));

            DrawIris(frame, landmarks, w, h,
                FaceMeshConnections.IrisCenterRight,
                FaceMeshConnections.RightIris,
                new Scalar(0, 255, 255));
        }
    }

    private static void DrawIris(Mat frame,
        Google.Protobuf.Collections.RepeatedField<NormalizedLandmark> landmarks,
        int w, int h, int centerIdx, (int, int)[] boundaryConnections, Scalar color)
    {
        var center = landmarks[centerIdx];
        var cx = (int)(center.X * w);
        var cy = (int)(center.Y * h);

        // Estimate radius from average distance of boundary points to center
        var boundaryIndices = new HashSet<int>();
        foreach (var (a, b) in boundaryConnections)
        {
            boundaryIndices.Add(a);
            boundaryIndices.Add(b);
        }

        double totalDist = 0;
        int distCount = 0;
        foreach (var idx in boundaryIndices)
        {
            var lm = landmarks[idx];
            var dx = lm.X * w - cx;
            var dy = lm.Y * h - cy;
            totalDist += Math.Sqrt(dx * dx + dy * dy);
            distCount++;
        }

        int radius = distCount > 0 ? Math.Max(1, (int)(totalDist / distCount)) : 3;

        Cv2.Circle(frame, new Point(cx, cy), radius, color, 1, LineTypes.AntiAlias);
        Cv2.Circle(frame, new Point(cx, cy), 1, color, -1, LineTypes.AntiAlias);
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

            _poller?.Dispose();
            _poller = null;

            _graph.Dispose();
            _graph = null;
        }
    }
}
