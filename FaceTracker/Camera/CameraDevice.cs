using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;

namespace FaceFinderDemo.Camera;

/// <summary>
/// Image-processing pipeline source node that captures frames from a physical camera.
/// </summary>
/// <remarks>
/// Frames are read on a dedicated background thread named <c>CameraCapture</c>.
/// Each captured frame is cloned before being raised via
/// <see cref="ImageProcessor.ImageAvailable"/> so that the internal read buffer
/// can be reused on the next iteration without race conditions.
/// </remarks>
public class CameraDevice : ImageProcessor, IDisposable
{
    /// <summary>
    /// <see langword="true"/> while the capture thread is running and frames are being emitted.
    /// </summary>
    public bool IsCapturing => _isCapturing;

    VideoCapture? _capture;
    Thread? _captureThread;
    bool _disposed;
    volatile bool _isCapturing; // volatile: written by StopCamera on the UI thread, read by the capture thread

    /// <summary>
    /// Opens the camera at <paramref name="cameraIndex"/> and starts the capture loop on a
    /// background thread. Does nothing if capture is already running.
    /// </summary>
    /// <param name="cameraIndex">
    /// Zero-based OpenCV camera index (corresponds to the position in the list returned by
    /// <see cref="DeviceEnumerator.GetDeviceNames"/>).
    /// </param>
    public void StartCamera(int cameraIndex)
    {
        if (_isCapturing) return;

        _capture = new VideoCapture(cameraIndex);
        if (!_capture.IsOpened())
        {
            _capture.Dispose();
            _capture = null;
            return;
        }

        _isCapturing = true;
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "CameraCapture" };
        _captureThread.Start();
    }

    /// <summary>
    /// Background capture loop: reads frames from <see cref="_capture"/> and raises
    /// <see cref="ImageProcessor.ImageAvailable"/> for each non-empty frame.
    /// </summary>
    private void CaptureLoop()
    {
        using var frame = new Mat();
        while (_isCapturing)
        {
            if (_capture == null || !_capture.IsOpened()) break;
            if (_capture.Read(frame) && !frame.Empty())
            {
                // Clone so the downstream pipeline owns its copy while we reuse the buffer.
                OnImageAvailable(frame.Clone());
            }
        }
    }

    /// <summary>
    /// Stops the capture loop, blocks until the background thread exits, and releases
    /// the camera handle. Does nothing if capture is not running.
    /// </summary>
    public void StopCamera()
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        _captureThread?.Join(); // Wait until the thread has fully exited before releasing resources
        _captureThread = null;
        _capture?.Dispose();
        _capture = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        StopCamera();
        _disposed = true;
    }

    /// <summary>
    /// No-op: <see cref="CameraDevice"/> is a source node and does not receive frames from upstream.
    /// </summary>
    protected override void OnImageReceived(Mat image) { }
}
