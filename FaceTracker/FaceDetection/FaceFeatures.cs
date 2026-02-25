using OpenCvSharp;

namespace FaceFinderDemo.FaceDetection;

public class FaceFeatures
{
    public FaceFeatures(Rect face, int frameWidth, int frameHeight)
    {
        FaceLocation = face;
        CalcProbableEyeLocation(frameWidth, frameHeight);
        CalcProbableNoseLocation(frameWidth, frameHeight);
        CalcProbableMouthLocation(frameWidth, frameHeight);
    }

    private void CalcProbableEyeLocation(int width, int height)
    {
        var loc = FaceLocation;
        int origHeight = loc.Height;
        loc.Height = (int)(loc.Height / 2.7f);
        int shiftY = (int)((origHeight / 1.7) - loc.Height);
        loc.Y += shiftY;
        ProbableEyeLocation = FixBoundings(loc, width, height);
    }

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

    Rect FixBoundings(Rect rect, int width, int height)
    {
        if (rect.Left < 0) rect.X = 0;
        if (rect.Top < 0) rect.Y = 0;
        if (rect.Bottom > height) rect.Height = rect.Height - (rect.Bottom - height);
        if (rect.Right > width) rect.Width = rect.Width - (rect.Right - width);
        return rect;
    }

    public bool IsValid =>
        LeftEyeLocation != default(Rect) && RightEyeLocation != default(Rect) &&
        NoseLocation != default(Rect) && MouthLocation != default(Rect);

    public Rect ProbableEyeLocation { get; private set; }
    public Rect ProbableNoseLocation { get; private set; }
    public Rect ProbableMouthLocation { get; private set; }
    public Rect FaceLocation { get; private set; }
    public Rect LeftEyeLocation { get; private set; }
    public Rect RightEyeLocation { get; private set; }
    public Rect NoseLocation { get; private set; }
    public Rect MouthLocation { get; private set; }

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

    public void DrawToImage(Mat image, bool includeInterestAreas)
    {
        int thickness = 2;
        // Red for face
        Cv2.Rectangle(image, FaceLocation, new Scalar(0, 0, 255), thickness);

        if (LeftEyeLocation != default(Rect))
            Cv2.Rectangle(image, LeftEyeLocation, new Scalar(0, 255, 255), thickness);

        if (RightEyeLocation != default(Rect))
            Cv2.Rectangle(image, RightEyeLocation, new Scalar(0, 255, 255), thickness);

        if (NoseLocation != default(Rect))
            Cv2.Rectangle(image, NoseLocation, new Scalar(0, 255, 0), thickness);

        if (MouthLocation != default(Rect))
            Cv2.Rectangle(image, MouthLocation, new Scalar(255, 0, 0), thickness);

        if (includeInterestAreas)
        {
            Cv2.Rectangle(image, ProbableEyeLocation, new Scalar(255, 0, 255), thickness);
            Cv2.Rectangle(image, ProbableNoseLocation, new Scalar(255, 0, 255), thickness);
            Cv2.Rectangle(image, ProbableMouthLocation, new Scalar(255, 0, 255), thickness);
        }
    }
}
