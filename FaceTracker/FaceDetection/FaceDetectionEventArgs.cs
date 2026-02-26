namespace FaceFinderDemo.FaceDetection;

/// <summary>
/// Event arguments for <see cref="FaceDetectorDevice.FaceDetectorStateChanged"/>.
/// Carries the detection lifecycle phase and, once complete, the list of detected faces.
/// </summary>
public class FaceDetectionEventArgs : EventArgs
{
    /// <summary>
    /// <see langword="true"/> when detection has just started and the frame is being analysed;
    /// <see langword="false"/> when detection has finished and <see cref="Faces"/> is populated.
    /// </summary>
    public bool Starting { get; }

    /// <summary>
    /// The detected faces, or <see langword="null"/> when <see cref="Starting"/> is <see langword="true"/>.
    /// Each element contains the bounding box and feature regions for one face.
    /// </summary>
    public List<FaceFeatures>? Faces { get; }

    /// <summary>
    /// Elapsed time of the detection pass in milliseconds.
    /// Zero when <see cref="Starting"/> is <see langword="true"/>.
    /// </summary>
    public int DetectionTime { get; }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="starting"><see langword="true"/> at detection start; <see langword="false"/> at completion.</param>
    /// <param name="faces">Detected faces; may be <see langword="null"/> when <paramref name="starting"/> is <see langword="true"/>.</param>
    /// <param name="detectionTime">Elapsed milliseconds for this detection pass; zero on start.</param>
    public FaceDetectionEventArgs(bool starting, List<FaceFeatures>? faces, int detectionTime)
    {
        Starting = starting;
        Faces = faces;
        DetectionTime = detectionTime;
    }
}
