using OpenCvSharp;

namespace FaceFinderDemo.ImageProcessing;

/// <summary>
/// Abstract base class for all nodes in the image-processing pipeline.
/// </summary>
/// <remarks>
/// Nodes form a linear chain:
/// <list type="bullet">
///   <item>Call <see cref="AttachSource"/> to subscribe this node to an upstream
///   <see cref="ImageProcessor"/>; every frame it emits is forwarded to <see cref="OnImageReceived"/>.</item>
///   <item>Override <see cref="OnImageReceived"/> to process the frame, then call
///   <see cref="OnImageAvailable"/> to pass the result to downstream subscribers.</item>
/// </list>
/// Source nodes that originate frames (cameras, files) implement <see cref="OnImageReceived"/>
/// as a no-op and invoke <see cref="OnImageAvailable"/> directly from their capture loops.
/// </remarks>
public abstract class ImageProcessor
{
    /// <summary>
    /// Raised whenever this node produces a frame ready for downstream consumption.
    /// </summary>
    public EventHandler<ImageAvailableEventArgs>? ImageAvailable;

    // The upstream source whose ImageAvailable event this node is currently subscribed to.
    ImageProcessor? _source;

    /// <summary>
    /// Connects this node to <paramref name="source"/> as its upstream input.
    /// Any previously attached source is automatically detached first.
    /// </summary>
    /// <param name="source">The upstream <see cref="ImageProcessor"/> to subscribe to.</param>
    public void AttachSource(ImageProcessor source)
    {
        DetachSource();
        _source = source;
        _source.ImageAvailable += ImageAvailableFromSource;
    }

    /// <summary>
    /// Disconnects from the current upstream source. Safe to call when no source is attached.
    /// </summary>
    public void DetachSource()
    {
        if (_source == null) return;
        _source.ImageAvailable -= ImageAvailableFromSource;
        _source = null;
    }

    private void ImageAvailableFromSource(object? sender, ImageAvailableEventArgs e) =>
        OnImageReceived(e.Image);

    /// <summary>
    /// Called on every frame received from the upstream source.
    /// Override to apply processing, then call <see cref="OnImageAvailable"/> to pass the
    /// result to downstream subscribers. Source nodes that generate their own frames may
    /// implement this as a no-op.
    /// </summary>
    /// <param name="image">The incoming frame. Do not dispose; clone if you need to retain it.</param>
    protected abstract void OnImageReceived(Mat image);

    /// <summary>
    /// Raises <see cref="ImageAvailable"/> to deliver <paramref name="image"/> to all
    /// downstream subscribers. Call this at the end of <see cref="OnImageReceived"/>
    /// (or from a capture loop in a source node) once processing is complete.
    /// </summary>
    /// <param name="image">The frame to deliver downstream.</param>
    protected void OnImageAvailable(Mat image) =>
        ImageAvailable?.Invoke(this, new ImageAvailableEventArgs(image));
}
