using System;
using OpenCvSharp;

namespace FaceFinderDemo.ImageProcessing;

public abstract class ImageProcessor
{
    public EventHandler<ImageAvailableEventArgs>? ImageAvailable;
    ImageProcessor? _source;

    public void AttachSource(ImageProcessor source)
    {
        DetachSource();
        _source = source;
        _source.ImageAvailable += ImageAvailableFromSource;
    }

    public void DetachSource()
    {
        if (_source == null) return;
        _source.ImageAvailable -= ImageAvailableFromSource;
        _source = null;
    }

    private void ImageAvailableFromSource(object? sender, ImageAvailableEventArgs e) =>
        OnImageReceived(e.Image);

    protected abstract void OnImageReceived(Mat image);

    protected void OnImageAvailable(Mat image) =>
        ImageAvailable?.Invoke(this, new ImageAvailableEventArgs(image));
}
