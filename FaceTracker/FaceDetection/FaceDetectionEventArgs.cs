namespace FaceFinderDemo.FaceDetection;

public class FaceDetectionEventArgs : EventArgs
{
    public bool Starting { get; }
    public List<FaceFeatures>? Faces { get; }
    public int DetectionTime { get; }

    public FaceDetectionEventArgs(bool starting, List<FaceFeatures>? faces, int detectionTime)
    {
        Starting = starting;
        Faces = faces;
        DetectionTime = detectionTime;
    }
}
