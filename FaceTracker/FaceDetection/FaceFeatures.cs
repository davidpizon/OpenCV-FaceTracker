using OpenCvSharp;

namespace FaceFinderDemo.FaceDetection;

/// <summary>
/// Stores the bounding-box geometry for a detected face and its facial features,
/// and provides methods for populating and drawing those regions.
/// </summary>
/// <remarks>
/// On construction the <see cref="ProbableEyeLocation"/>, <see cref="ProbableNoseLocation"/>,
/// and <see cref="ProbableMouthLocation"/> regions of interest are estimated from the
/// face rectangle using fixed proportional offsets. After running sub-classifiers within
/// those ROIs, call <see cref="AddEyes"/>, <see cref="AddNose"/>, and <see cref="AddMouth"/>
/// to store the confirmed detections in frame-relative coordinates.
/// </remarks>
public class FaceFeatures
{
    /// <summary>
    /// Initialises face geometry and pre-calculates probable feature regions from the
    /// face bounding box.
    /// </summary>
    /// <param name="face">The face bounding box in frame pixel coordinates.</param>
    /// <param name="frameWidth">Width of the source frame in pixels, used to clamp ROIs.</param>
    /// <param name="frameHeight">Height of the source frame in pixels, used to clamp ROIs.</param>
    public FaceFeatures(Rect face, int frameWidth, int frameHeight)
    {
        FaceLocation = face;
        CalcProbableEyeLocation(frameWidth, frameHeight);
        CalcProbableNoseLocation(frameWidth, frameHeight);
        CalcProbableMouthLocation(frameWidth, frameHeight);
    }

    /// <summary>
    /// Estimates the eye strip ROI as the upper ~37% of the face rectangle,
    /// shifted down to align with the typical eye position.
    /// </summary>
    private void CalcProbableEyeLocation(int width, int height)
    {
        var loc = FaceLocation;
        int origHeight = loc.Height;
        loc.Height = (int)(loc.Height / 2.7f);
        int shiftY = (int)((origHeight / 1.7) - loc.Height);
        loc.Y += shiftY;
        ProbableEyeLocation = FixBoundings(loc, width, height);
    }

    /// <summary>
    /// Estimates the mouth ROI as the centre-bottom quarter of the face rectangle.
    /// </summary>
    private void CalcProbableMouthLocation(int width, int height)
    {
        var loc = FaceLocation;
        loc.Width /= 2;
        loc.Height /= 3;
        int shiftX = (FaceLocation.Width - loc.Width) / 2;
        int shiftY = loc.Height * 2;
        loc.X += shiftX;
        loc.Y += shiftY;
        ProbableMouthLocation = FixBoundings(loc, width, height);
    }

    /// <summary>
    /// Estimates the nose ROI as a ~43% scaled-down copy of the face rectangle, centred within it.
    /// </summary>
    private void CalcProbableNoseLocation(int width, int height)
    {
        var loc = FaceLocation;
        loc.Width = (int)(0.43 * loc.Width);
        loc.Height = (int)(0.43 * loc.Height);
        int shiftX = (FaceLocation.Width - loc.Width) / 2;
        int shiftY = (FaceLocation.Height - loc.Height) / 2;
        loc.X += shiftX;
        loc.Y += shiftY;
        ProbableNoseLocation = FixBoundings(loc, width, height);
    }

    /// <summary>
    /// Clamps <paramref name="rect"/> so it does not extend beyond the frame boundaries.
    /// </summary>
    Rect FixBoundings(Rect rect, int width, int height)
    {
        if (rect.Left < 0) rect.X = 0;
        if (rect.Top < 0) rect.Y = 0;
        if (rect.Bottom > height) rect.Height = rect.Height - (rect.Bottom - height);
        if (rect.Right > width) rect.Width = rect.Width - (rect.Right - width);
        return rect;
    }

    /// <summary>
    /// <see langword="true"/> when all four confirmed feature locations have been populated
    /// (both eyes, nose, and mouth). Useful for filtering out partial detections.
    /// </summary>
    public bool IsValid =>
        LeftEyeLocation != default(Rect) && RightEyeLocation != default(Rect) &&
        NoseLocation != default(Rect) && MouthLocation != default(Rect);

    /// <summary>Estimated ROI used by the eye sub-classifier, in frame coordinates.</summary>
    public Rect ProbableEyeLocation { get; private set; }

    /// <summary>Estimated ROI used by the nose sub-classifier, in frame coordinates.</summary>
    public Rect ProbableNoseLocation { get; private set; }

    /// <summary>Estimated ROI used by the mouth sub-classifier, in frame coordinates.</summary>
    public Rect ProbableMouthLocation { get; private set; }

    /// <summary>Bounding box of the detected face, in frame coordinates.</summary>
    public Rect FaceLocation { get; private set; }

    /// <summary>Confirmed left-eye bounding box in frame coordinates, or <c>default</c> if not detected.</summary>
    public Rect LeftEyeLocation { get; private set; }

    /// <summary>Confirmed right-eye bounding box in frame coordinates, or <c>default</c> if not detected.</summary>
    public Rect RightEyeLocation { get; private set; }

    /// <summary>Confirmed nose bounding box in frame coordinates, or <c>default</c> if not detected.</summary>
    public Rect NoseLocation { get; private set; }

    /// <summary>Confirmed mouth bounding box in frame coordinates, or <c>default</c> if not detected.</summary>
    public Rect MouthLocation { get; private set; }

    /// <summary>
    /// Stores confirmed eye detections from a sub-classifier run inside <see cref="ProbableEyeLocation"/>.
    /// Translates ROI-relative coordinates to frame coordinates before storing.
    /// </summary>
    /// <param name="eyes">Rects returned by the eye classifier; only the first two are used.</param>
    public void AddEyes(Rect[] eyes)
    {
        if (eyes.Length > 0)
        {
            var r = eyes[0];
            r.X += ProbableEyeLocation.X;
            r.Y += ProbableEyeLocation.Y;
            LeftEyeLocation = r;
        }
        if (eyes.Length > 1)
        {
            var r = eyes[1];
            r.X += ProbableEyeLocation.X;
            r.Y += ProbableEyeLocation.Y;
            RightEyeLocation = r;
        }
    }

    /// <summary>
    /// Stores a confirmed nose detection from a sub-classifier run inside <see cref="ProbableNoseLocation"/>.
    /// Translates ROI-relative coordinates to frame coordinates before storing.
    /// </summary>
    /// <param name="nose">Rects returned by the nose classifier; only the first is used.</param>
    public void AddNose(Rect[] nose)
    {
        if (nose.Length > 0)
        {
            var r = nose[0];
            r.X += ProbableNoseLocation.X;
            r.Y += ProbableNoseLocation.Y;
            NoseLocation = r;
        }
    }

    /// <summary>
    /// Stores a confirmed mouth detection from a sub-classifier run inside <see cref="ProbableMouthLocation"/>.
    /// Translates ROI-relative coordinates to frame coordinates before storing.
    /// </summary>
    /// <param name="mouth">Rects returned by the mouth classifier; only the first is used.</param>
    public void AddMouth(Rect[] mouth)
    {
        if (mouth.Length > 0)
        {
            var r = mouth[0];
            r.X += ProbableMouthLocation.X;
            r.Y += ProbableMouthLocation.Y;
            MouthLocation = r;
        }
    }

    /// <summary>
    /// Draws color-coded bounding boxes for all detected regions onto <paramref name="image"/>.
    /// </summary>
    /// <param name="image">The frame to draw onto (modified in-place, BGR color space).</param>
    /// <param name="includeInterestAreas">
    /// When <see langword="true"/>, the estimated ROI rectangles (<see cref="ProbableEyeLocation"/>,
    /// <see cref="ProbableNoseLocation"/>, <see cref="ProbableMouthLocation"/>) are also
    /// drawn in magenta for debugging.
    /// </param>
    public void DrawToImage(Mat image, bool includeInterestAreas)
    {
        int thickness = 2;
        // Red for face
        Cv2.Rectangle(image, FaceLocation, new Scalar(0, 0, 255), thickness);

        // Yellow for eyes
        if (LeftEyeLocation != default(Rect))
            Cv2.Rectangle(image, LeftEyeLocation, new Scalar(0, 255, 255), thickness);

        if (RightEyeLocation != default(Rect))
            Cv2.Rectangle(image, RightEyeLocation, new Scalar(0, 255, 255), thickness);

        // Green for nose
        if (NoseLocation != default(Rect))
            Cv2.Rectangle(image, NoseLocation, new Scalar(0, 255, 0), thickness);

        // Blue for mouth
        if (MouthLocation != default(Rect))
            Cv2.Rectangle(image, MouthLocation, new Scalar(255, 0, 0), thickness);

        // Magenta for estimated interest areas (debug)
        if (includeInterestAreas)
        {
            Cv2.Rectangle(image, ProbableEyeLocation, new Scalar(255, 0, 255), thickness);
            Cv2.Rectangle(image, ProbableNoseLocation, new Scalar(255, 0, 255), thickness);
            Cv2.Rectangle(image, ProbableMouthLocation, new Scalar(255, 0, 255), thickness);
        }
    }
}
