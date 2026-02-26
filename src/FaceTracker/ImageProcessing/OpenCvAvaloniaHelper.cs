using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCvSharp;

namespace FaceFinderDemo.ImageProcessing;

/// <summary>
/// Provides utility methods for converting OpenCV image types to Avalonia-compatible bitmap types.
/// </summary>
public static class OpenCvAvaloniaHelper
{
    /// <summary>
    /// Converts an OpenCV <see cref="Mat"/> to an Avalonia <see cref="WriteableBitmap"/>.
    /// </summary>
    /// <remarks>
    /// The output bitmap always uses <see cref="PixelFormats.Bgr24"/> at 96 DPI.
    /// Grayscale (1-channel) and BGRA (4-channel) inputs are converted to BGR before copying.
    /// The caller does not need to dispose the input <paramref name="mat"/>.
    /// </remarks>
    /// <param name="mat">The source OpenCV image. May be 1, 3, or 4 channels.</param>
    /// <returns>
    /// A new <see cref="WriteableBitmap"/> containing the pixel data, or <see langword="null"/>
    /// if <paramref name="mat"/> is null or empty.
    /// </returns>
    public static WriteableBitmap? MatToAvaloniaBitmap(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;

        // Avalonia's Bgr24 format requires exactly 3 channels in BGR order.
        // Convert grayscale and BGRA inputs; pass 3-channel mats through unchanged.
        Mat bgr;
        bool needDispose = false;
        if (mat.Channels() == 1)
        {
            // Grayscale ? BGR (replicate the single channel across R, G, B)
            bgr = new Mat();
            Cv2.CvtColor(mat, bgr, ColorConversionCodes.GRAY2BGR);
            needDispose = true;
        }
        else if (mat.Channels() == 4)
        {
            // BGRA ? BGR (drop the alpha channel)
            bgr = new Mat();
            Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
            needDispose = true;
        }
        else
        {
            // Already 3-channel BGR; use directly without an extra allocation
            bgr = mat;
        }

        int width = bgr.Width;
        int height = bgr.Height;

        // Allocate the Avalonia bitmap; Lock() gives a raw pointer to its pixel buffer.
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormats.Bgr24,
            AlphaFormat.Opaque);

        using var locked = bitmap.Lock();
        long srcStride = bgr.Step();   // bytes per row in the OpenCV Mat (may include padding)
        long dstStride = locked.RowBytes; // bytes per row in the Avalonia bitmap buffer

        if (srcStride == dstStride)
        {
            // Strides match: copy the entire pixel buffer in one operation.
            unsafe
            {
                Buffer.MemoryCopy(bgr.Data.ToPointer(), locked.Address.ToPointer(),
                    srcStride * height, srcStride * height);
            }
        }
        else
        {
            // Strides differ (e.g. OpenCV row padding ? Avalonia row alignment):
            // copy one row at a time so that each row lands at the correct destination offset.
            unsafe
            {
                for (int row = 0; row < height; row++)
                {
                    var src = (void*)(bgr.Data.ToInt64() + row * srcStride);
                    var dst = (void*)(locked.Address.ToInt64() + row * dstStride);
                    Buffer.MemoryCopy(src, dst, dstStride, srcStride);
                }
            }
        }

        // Release the temporary BGR Mat if one was allocated during channel conversion.
        if (needDispose) bgr.Dispose();

        return bitmap;
    }
}
