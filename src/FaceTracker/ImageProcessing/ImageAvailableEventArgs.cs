using OpenCvSharp;

namespace FaceFinderDemo.ImageProcessing;

/// <summary>
/// Carries a single <see cref="OpenCvSharp.Mat"/> frame through the image-processing pipeline
/// via the <see cref="ImageProcessor.ImageAvailable"/> event.
/// </summary>
/// <remarks>
/// The <see cref="Image"/> reference is owned by the sender. Downstream handlers that need to
/// retain the frame beyond the scope of the event callback must call
/// <see cref="OpenCvSharp.Mat.Clone"/> to obtain their own copy.
/// </remarks>
public class ImageAvailableEventArgs : EventArgs
{
    /// <summary>
    /// The frame delivered by the upstream pipeline node.
    /// Do not dispose this instance; call <see cref="OpenCvSharp.Mat.Clone"/> if you need to retain it.
    /// </summary>
    public Mat Image { get; }

    /// <summary>
    /// Initialises a new instance carrying <paramref name="image"/>.
    /// </summary>
    /// <param name="image">The frame to deliver. The caller retains ownership.</param>
    public ImageAvailableEventArgs(Mat image)
    {
        Image = image;
    }
}
