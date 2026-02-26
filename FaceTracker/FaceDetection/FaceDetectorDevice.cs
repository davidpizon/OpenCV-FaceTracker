using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;
using System.Diagnostics;
using System.Timers;

namespace FaceFinderDemo.FaceDetection;

/// <summary>
/// Image-processing pipeline processor that detects faces and facial features using
/// OpenCV Haar cascade classifiers.
/// </summary>
/// <remarks>
/// Detection can run on every frame (<see cref="DetectionModes.AllFrames"/>), on a
/// periodic timer (<see cref="DetectionModes.Periodic"/>), on demand
/// (<see cref="DetectionModes.Manual"/>), or not at all (<see cref="DetectionModes.Disabled"/>).
/// For <c>Periodic</c> and <c>Manual</c> modes the detection runs asynchronously on a
/// thread-pool thread so that the capture frame rate is not affected.
/// Each processed frame is forwarded downstream via <see cref="ImageProcessor.ImageAvailable"/>
/// with optional bounding-box overlays drawn directly on the image.
/// </remarks>
public class FaceDetectorDevice : ImageProcessor, IDisposable
{
    /// <summary>
    /// Gets or sets the current detection mode.
    /// Setting this property also re-applies <see cref="DetectionPeriod"/> so the
    /// internal notification timer is started or stopped accordingly.
    /// </summary>
    public DetectionModes DetectionMode
    {
        get => _detectionMode;
        set
        {
            _detectionMode = value;
            DetectionPeriod = _detectionPeriod;
        }
    }

    /// <summary>
    /// Gets or sets the interval between automatic detection passes when
    /// <see cref="DetectionMode"/> is <see cref="DetectionModes.Periodic"/>.
    /// Changes take effect immediately; the internal timer is restarted if necessary.
    /// </summary>
    public TimeSpan DetectionPeriod
    {
        get => _detectionPeriod;
        set
        {
            _detectionPeriod = value;
            if (_detectionMode == DetectionModes.Periodic)
            {
                _detectionNotifyTimer.Interval = _detectionPeriod.TotalMilliseconds;
                _detectionNotifyTimer.Start();
            }
            else
            {
                _detectionNotifyTimer.Stop();
            }
        }
    }

    /// <summary>
    /// When <see langword="true"/>, confirmed detection bounding boxes are drawn onto each
    /// frame before it is forwarded downstream.
    /// </summary>
    public bool DrawDetection { get; set; }

    /// <summary>
    /// When <see langword="true"/> and <see cref="DrawDetection"/> is also enabled, the
    /// estimated feature-search ROIs (eye strip, nose area, mouth area) are drawn in magenta.
    /// </summary>
    public bool DrawProbableAreas { get; set; }

    /// <summary>
    /// Raised when a detection pass starts or finishes.
    /// Use <see cref="FaceDetectionEventArgs.Starting"/> to distinguish the two phases.
    /// </summary>
    public EventHandler<FaceDetectionEventArgs>? FaceDetectorStateChanged;

    /// <summary>Controls when the face detection algorithm is invoked.</summary>
    public enum DetectionModes
    {
        /// <summary>Detection is inactive; any previous results are cleared.</summary>
        Disabled,
        /// <summary>Detection runs automatically at the interval set by <see cref="DetectionPeriod"/>.</summary>
        Periodic,
        /// <summary>Detection runs on every received frame (synchronous; may reduce frame rate).</summary>
        AllFrames,
        /// <summary>Detection runs only when explicitly triggered by <see cref="ManualDetect"/>.</summary>
        Manual
    }

    bool _disposed;
    CascadeClassifier? _faceClassifier;
    CascadeClassifier? _eyeClassifier;
    CascadeClassifier? _mouthClassifier;
    CascadeClassifier? _noseClassifier;

    List<FaceFeatures> _lastDetectedFaces = new();
    bool _detectingInProgress;
    bool _detectNextFrame;
    DetectionModes _detectionMode;
    System.Timers.Timer _detectionNotifyTimer;
    TimeSpan _detectionPeriod;

    /// <summary>Resolves a cascade filename to its full path under <c>Resources/haarcascades/</c>.</summary>
    static string CascadePath(string filename) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "haarcascades", filename);

    /// <summary>
    /// Loads all four Haar cascade classifiers and initialises the detection timer.
    /// Detection starts in <see cref="DetectionModes.Disabled"/> mode with a 500 ms period.
    /// </summary>
    public FaceDetectorDevice()
    {
        _faceClassifier = new CascadeClassifier(CascadePath("frontalface_alt.xml"));
        _eyeClassifier = new CascadeClassifier(CascadePath("eye.xml"));
        _mouthClassifier = new CascadeClassifier(CascadePath("mouth.xml"));
        _noseClassifier = new CascadeClassifier(CascadePath("nose.xml"));

        _detectionNotifyTimer = new System.Timers.Timer();
        _detectionNotifyTimer.Elapsed += DetectionNotifyTimerOnElapsed;
        DetectionMode = DetectionModes.Disabled;
        DetectionPeriod = TimeSpan.FromMilliseconds(500);
    }

    // Timer callback — sets the flag so the next received frame triggers a detection pass.
    void DetectionNotifyTimerOnElapsed(object? sender, ElapsedEventArgs e) =>
        _detectNextFrame = true;

    /// <summary>
    /// Schedules a single detection pass on the next received frame.
    /// Useful when <see cref="DetectionMode"/> is <see cref="DetectionModes.Manual"/>.
    /// </summary>
    public void ManualDetect() => _detectNextFrame = true;

    protected override void OnImageReceived(Mat image)
    {
        if (_detectionMode == DetectionModes.AllFrames)
        {
            DetectFaces(image);
        }
        else if (_detectionMode == DetectionModes.Periodic || _detectionMode == DetectionModes.Manual)
        {
            if (_detectNextFrame && !_detectingInProgress)
            {
                _detectNextFrame = false;
                var imgCopy = image.Clone();
                Task.Factory.StartNew(() => DetectFaces(imgCopy));
            }
        }
        else if (_detectionMode == DetectionModes.Disabled)
        {
            _lastDetectedFaces.Clear();
        }

        if (DrawDetection)
        {
            foreach (var f in _lastDetectedFaces)
                f.DrawToImage(image, DrawProbableAreas);
        }

        OnImageAvailable(image);
    }

    /// <summary>
    /// Runs the full Haar cascade detection pipeline on <paramref name="image"/>:
    /// converts to grayscale, equalises the histogram, detects faces, then runs
    /// sub-classifiers for nose, eyes, and mouth within each face's estimated ROIs.
    /// </summary>
    /// <remarks>
    /// In <see cref="DetectionModes.Periodic"/> and <see cref="DetectionModes.Manual"/> modes
    /// this method receives a cloned frame and disposes it when done.
    /// </remarks>
    private void DetectFaces(Mat image)
    {
        if (_faceClassifier == null || _faceClassifier.Empty()) return;

        OnFaceDetectionStateChanged(true);
        _detectingInProgress = true;
        var detectedFaces = new List<FaceFeatures>();

        var watch = Stopwatch.StartNew();
        using var grayImage = new Mat();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(grayImage, grayImage);

        var facesDetected = _faceClassifier.DetectMultiScale(
            grayImage, 1.1, 3, HaarDetectionTypes.ScaleImage, new Size(40, 40));

        foreach (var faceRect in facesDetected)
        {
            var face = new FaceFeatures(faceRect, grayImage.Cols, grayImage.Rows);

            if (_noseClassifier != null && !_noseClassifier.Empty())
            {
                using var noseRoi = new Mat(grayImage, face.ProbableNoseLocation);
                face.AddNose(_noseClassifier.DetectMultiScale(noseRoi, 1.13, 3, HaarDetectionTypes.ScaleImage, new Size(10, 10)));
            }

            if (_eyeClassifier != null && !_eyeClassifier.Empty())
            {
                using var eyeRoi = new Mat(grayImage, face.ProbableEyeLocation);
                face.AddEyes(_eyeClassifier.DetectMultiScale(eyeRoi, 1.13, 3, HaarDetectionTypes.ScaleImage, new Size(10, 10)));
            }

            if (_mouthClassifier != null && !_mouthClassifier.Empty())
            {
                using var mouthRoi = new Mat(grayImage, face.ProbableMouthLocation);
                face.AddMouth(_mouthClassifier.DetectMultiScale(mouthRoi, 1.13, 3, HaarDetectionTypes.ScaleImage, new Size(10, 20)));
            }

            detectedFaces.Add(face);
        }

        // Dispose imgCopy if this was an async call (it was cloned for us)
        if (image != null && _detectionMode != DetectionModes.AllFrames)
            image.Dispose();

        watch.Stop();
        _lastDetectedFaces = detectedFaces;
        OnFaceDetectionStateChanged(false, detectedFaces, (int)watch.ElapsedMilliseconds);
        _detectingInProgress = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _detectionNotifyTimer.Dispose();
        _faceClassifier?.Dispose();
        _eyeClassifier?.Dispose();
        _mouthClassifier?.Dispose();
        _noseClassifier?.Dispose();
        _disposed = true;
    }

    // Fires FaceDetectorStateChanged with the supplied parameters.
    private void OnFaceDetectionStateChanged(bool starting, List<FaceFeatures>? faces = null, int detectionTime = 0)
    {
        FaceDetectorStateChanged?.Invoke(this, new FaceDetectionEventArgs(starting, faces, detectionTime));
    }

    /// <summary>Clears the cached list of last-detected faces.</summary>
    public void ResetDetections() => _lastDetectedFaces.Clear();
}
