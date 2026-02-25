using System;
using System.Threading;
using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;

namespace FaceFinderDemo.Camera;

public class CameraDevice : ImageProcessor, IDisposable
{
    public bool IsCapturing => _isCapturing;

    VideoCapture? _capture;
    Thread? _captureThread;
    bool _disposed;
    volatile bool _isCapturing;

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

    private void CaptureLoop()
    {
        using var frame = new Mat();
        while (_isCapturing)
        {
            if (_capture == null || !_capture.IsOpened()) break;
            if (_capture.Read(frame) && !frame.Empty())
            {
                OnImageAvailable(frame.Clone());
            }
        }
    }

    public void StopCamera()
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        _captureThread?.Join(2000);
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopCamera();
        _disposed = true;
    }

    protected override void OnImageReceived(Mat image) { }
}
