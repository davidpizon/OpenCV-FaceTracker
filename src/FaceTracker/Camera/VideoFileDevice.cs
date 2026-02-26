using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;

namespace FaceFinderDemo.Camera;

/// <summary>
/// Image-processing pipeline source node that plays back a video file in a continuous loop.
/// </summary>
/// <remarks>
/// Playback is paced to the video's native frame rate. If the FPS metadata is absent or
/// outside the valid 0–120 range, 25 FPS is used as a fallback. When the end of the file
/// is reached the playback position is reset to the start so playback loops indefinitely
/// until <see cref="StopPlayback"/> is called.
/// </remarks>
public class VideoFileDevice : ImageProcessor, IDisposable
{
    VideoCapture? _capture;
    Thread? _playThread;
    bool _disposed;
    volatile bool _isPlaying; // volatile: written by StopPlayback on the UI thread, read by the playback thread
    string? _filePath;

    /// <summary>
    /// Validates that the file at <paramref name="filePath"/> can be opened by OpenCV and
    /// stores the path for use by <see cref="StartPlayback"/>. The file handle is released
    /// immediately after validation.
    /// </summary>
    /// <param name="filePath">Full path to the video file.</param>
    /// <returns>
    /// <see langword="true"/> if OpenCV can open the file; <see langword="false"/> otherwise.
    /// </returns>
    public bool LoadFromFile(string filePath)
    {
        _filePath = filePath;
        using var testCap = new VideoCapture(filePath);
        return testCap.IsOpened();
    }

    /// <summary>
    /// Opens the previously loaded file and starts the playback loop on a background thread.
    /// Does nothing if playback is already running or no file has been loaded.
    /// </summary>
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

    /// <summary>
    /// Background playback loop: reads frames, emits them downstream paced to the native FPS,
    /// and seeks back to the start when the video ends for seamless looping.
    /// </summary>
    private void PlaybackLoop()
    {
        if (_capture == null) return;

        double fps = _capture.Fps;
        if (fps <= 0 || fps > 120) fps = 25; // fallback for missing or invalid FPS metadata
        int delayMs = (int)(1000.0 / fps);

        using var frame = new Mat();
        while (_isPlaying)
        {
            if (!_capture.Read(frame) || frame.Empty())
            {
                // End of file — seek back to the start for seamless looping
                _capture.Set(VideoCaptureProperties.PosMsec, 0);
                continue;
            }
            OnImageAvailable(frame.Clone());
            Thread.Sleep(delayMs);
        }
    }

    /// <summary>
    /// Signals the playback loop to stop, waits up to 2 seconds for the thread to exit,
    /// and releases the video capture handle. Does nothing if not currently playing.
    /// </summary>
    public void StopPlayback()
    {
        if (!_isPlaying) return;
        _isPlaying = false;
        _playThread?.Join(2000);
        _capture?.Dispose();
        _capture = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        StopPlayback();
        _disposed = true;
    }

    /// <summary>
    /// No-op: <see cref="VideoFileDevice"/> is a source node and does not receive frames from upstream.
    /// </summary>
    protected override void OnImageReceived(Mat image) { }
}
