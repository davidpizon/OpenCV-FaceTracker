using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;
using System.Diagnostics;
using System.Timers;

namespace FaceFinderDemo.FaceDetection;

public class FaceDetectorDevice : ImageProcessor, IDisposable
{
    public DetectionModes DetectionMode
    {
        get => _detectionMode;
        set
        {
            _detectionMode = value;
            DetectionPeriod = _detectionPeriod;
        }
    }

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

    public bool DrawDetection { get; set; }
    public bool DrawProbableAreas { get; set; }

    public EventHandler<FaceDetectionEventArgs>? FaceDetectorStateChanged;

    public enum DetectionModes { Disabled, Periodic, AllFrames, Manual }

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

    static string CascadePath(string filename) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "haarcascades", filename);

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

    void DetectionNotifyTimerOnElapsed(object? sender, ElapsedEventArgs e) =>
        _detectNextFrame = true;

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

    private void OnFaceDetectionStateChanged(bool starting, List<FaceFeatures>? faces = null, int detectionTime = 0)
    {
        FaceDetectorStateChanged?.Invoke(this, new FaceDetectionEventArgs(starting, faces, detectionTime));
    }

    public void ResetDetections() => _lastDetectedFaces.Clear();
}
