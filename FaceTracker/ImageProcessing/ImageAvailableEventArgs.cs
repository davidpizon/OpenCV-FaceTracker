using System;
using OpenCvSharp;

namespace FaceFinderDemo.ImageProcessing;

public class ImageAvailableEventArgs : EventArgs
{
    public Mat Image { get; }

    public ImageAvailableEventArgs(Mat image)
    {
        Image = image;
    }
}
