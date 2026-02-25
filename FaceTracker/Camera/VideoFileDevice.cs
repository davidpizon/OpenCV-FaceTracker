using System;
using System.Threading;
using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;

namespace FaceFinderDemo.Camera;

public class VideoFileDevice : ImageProcessor, IDisposable
{
    public bool IsPlaying => _isPlaying;

    VideoCapture? _capture;
    Thread? _playThread;
    bool _disposed;
    volatile bool _isPlaying;
    string? _filePath;

    public bool LoadFromFile(string filePath)
    {
        _filePath = filePath;
        using var testCap = new VideoCapture(filePath);
        return testCap.IsOpened();
    }

    public void StartPlayback()
    {
        if (_isPlaying || _filePath == null) return;

        _capture = new VideoCapture(_filePath);
        if (!_capture.IsOpened())
        {
            _capture.Dispose();
            _capture = null;
            return;
        }

        _isPlaying = true;
        _playThread = new Thread(PlaybackLoop) { IsBackground = true, Name = "VideoPlayback" };
        _playThread.Start();
    }

    private void PlaybackLoop()
    {
        if (_capture == null) return;

        double fps = _capture.Fps;
        if (fps <= 0 || fps > 120) fps = 25;
        int delayMs = (int)(1000.0 / fps);

        using var frame = new Mat();
        while (_isPlaying)
        {
            if (!_capture.Read(frame) || frame.Empty())
            {
                // Loop back to beginning
                _capture.Set(VideoCaptureProperties.PosMsec, 0);
                continue;
            }
            OnImageAvailable(frame.Clone());
            Thread.Sleep(delayMs);
        }
    }

    public void StopPlayback()
    {
        if (!_isPlaying) return;
        _isPlaying = false;
        _playThread?.Join(2000);
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopPlayback();
        _disposed = true;
    }

    protected override void OnImageReceived(Mat image) { }
}
