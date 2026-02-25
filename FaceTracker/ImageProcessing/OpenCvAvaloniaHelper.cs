using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCvSharp;

namespace FaceFinderDemo.ImageProcessing;

public static class OpenCvAvaloniaHelper
{
    public static WriteableBitmap? MatToAvaloniaBitmap(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;

        Mat bgr;
        bool needDispose = false;
        if (mat.Channels() == 1)
        {
            bgr = new Mat();
            Cv2.CvtColor(mat, bgr, ColorConversionCodes.GRAY2BGR);
            needDispose = true;
        }
        else if (mat.Channels() == 4)
        {
            bgr = new Mat();
            Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
            needDispose = true;
        }
        else
        {
            bgr = mat;
        }

        int width = bgr.Width;
        int height = bgr.Height;

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormats.Bgr24,
            AlphaFormat.Opaque);

        using var locked = bitmap.Lock();
        long srcStride = bgr.Step();
        long dstStride = locked.RowBytes;

        if (srcStride == dstStride)
        {
            var totalBytes = (int)(srcStride * height);
            var buffer = new byte[totalBytes];
            Marshal.Copy(bgr.Data, buffer, 0, totalBytes);
            Marshal.Copy(buffer, 0, locked.Address, totalBytes);
        }
        else
        {
            var rowBuffer = new byte[srcStride];
            for (int row = 0; row < height; row++)
            {
                var src = new IntPtr(bgr.Data.ToInt64() + row * srcStride);
                var dst = new IntPtr(locked.Address.ToInt64() + row * dstStride);
                Marshal.Copy(src, rowBuffer, 0, (int)srcStride);
                Marshal.Copy(rowBuffer, 0, dst, (int)srcStride);
            }
        }

        if (needDispose) bgr.Dispose();

        return bitmap;
    }
}
