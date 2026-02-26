using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;
using System.Timers;

namespace FaceFinderDemo.Camera;

/// <summary>
/// Image-processing pipeline source node that re-emits a static image at a fixed interval.
/// </summary>
/// <remarks>
/// After <see cref="LoadFromFile"/> loads an image, <see cref="StartSending"/> starts a 100 ms
/// timer that repeatedly clones and emits the image via <see cref="ImageProcessor.ImageAvailable"/>.
/// The timer is stopped before the clone is taken and restarted after emission to prevent
/// overlapping callbacks if the downstream pipeline is slow.
/// </remarks>
public class ImageDevice : ImageProcessor, IDisposable
{
    bool _disposed;
    Mat? _image;
    bool _isSending;
    System.Timers.Timer _sendTimer;
    readonly object _sync = new(); // guards _image across LoadFromFile and SendTimerOnElapsed

    /// <summary>
    /// Initialises the device with a 100 ms emission interval.
    /// </summary>
    public ImageDevice()
    {
        _sendTimer = new System.Timers.Timer(100);
        _sendTimer.Elapsed += SendTimerOnElapsed;
    }

    /// <summary>
    /// Timer callback: stops the timer, clones the current image under the lock, emits the
    /// clone downstream, then restarts the timer. This pattern prevents re-entrant callbacks.
    /// </summary>
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

    /// <summary>
    /// Starts emitting the loaded image on the send timer. Does nothing if already sending.
    /// </summary>
    public void StartSending()
    {
        if (_isSending) return;
        _sendTimer.Start();
        _isSending = true;
    }

    /// <summary>
    /// Stops the send timer. Does nothing if not currently sending.
    /// </summary>
    public void StopSending()
    {
        if (!_isSending) return;
        _sendTimer.Stop();
        _isSending = false;
    }

    /// <summary>
    /// Loads a color image from <paramref name="imageFile"/> and stores it for emission.
    /// Thread-safe: acquires <see cref="_sync"/> while swapping the stored <see cref="Mat"/>.
    /// </summary>
    /// <param name="imageFile">Full path to the image file.</param>
    /// <returns>
    /// <see langword="true"/> if the image was loaded successfully;
    /// <see langword="false"/> if the file is empty or an exception occurs.
    /// </returns>
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _sendTimer.Dispose();
        _image?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// No-op: <see cref="ImageDevice"/> is a source node and does not receive frames from upstream.
    /// </summary>
    protected override void OnImageReceived(Mat image) { }
}
