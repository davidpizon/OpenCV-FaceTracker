using System;
using System.Timers;
using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;

namespace FaceFinderDemo.Camera;

public class ImageDevice : ImageProcessor, IDisposable
{
    public bool IsSending => _isSending;
    public int FrameRate { get; set; }

    bool _disposed;
    Mat? _image;
    bool _isSending;
    System.Timers.Timer _sendTimer;
    readonly object _sync = new();

    public ImageDevice()
    {
        _sendTimer = new System.Timers.Timer(100);
        _sendTimer.Elapsed += SendTimerOnElapsed;
    }

    void SendTimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_image == null) return;
        _sendTimer.Stop();
        Mat clone;
        lock (_sync)
        {
            if (_image == null) { _sendTimer.Start(); return; }
            clone = _image.Clone();
        }
        OnImageAvailable(clone);
        _sendTimer.Start();
    }

    public void StartSending()
    {
        if (_isSending) return;
        _sendTimer.Start();
        _isSending = true;
    }

    public void StopSending()
    {
        if (!_isSending) return;
        _sendTimer.Stop();
        _isSending = false;
    }

    public bool LoadFromFile(string imageFile)
    {
        lock (_sync)
        {
            try
            {
                var loaded = Cv2.ImRead(imageFile, ImreadModes.Color);
                if (loaded.Empty()) return false;
                _image?.Dispose();
                _image = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _sendTimer.Dispose();
        _image?.Dispose();
        _disposed = true;
    }

    protected override void OnImageReceived(Mat image) { }
}
