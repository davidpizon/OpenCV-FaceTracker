using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FaceFinderDemo.Camera;
using FaceFinderDemo.FaceDetection;
using FaceFinderDemo.ImageProcessing;
using OpenCvSharp;

namespace FaceFinderDemo;

/// <summary>
/// Main application window. Wires UI events to the image-processing pipeline and
/// displays the annotated video feed produced by <see cref="FaceMeshDevice"/>.
/// </summary>
public partial class MainWindow : Avalonia.Controls.Window
{
    // Pipeline nodes — FaceMesh is the single processor; the three sources are mutually exclusive.
    FaceMeshDevice _faceMesh;
    MainWindowViewModel _model;
    CameraDevice _camera;
    ImageDevice _image;
    VideoFileDevice _video;

    // Shown in the image control while no capture source is active.
    Bitmap? _placeholderBitmap;

    /// <summary>
    /// Creates all pipeline devices, subscribes to the face-mesh output event,
    /// and registers window lifecycle handlers.
    /// </summary>
    public MainWindow()
    {
        _model = new MainWindowViewModel();
        DataContext = _model;

        _faceMesh = new FaceMeshDevice();
        _faceMesh.OnStatus = msg => Dispatcher.UIThread.Post(() => _model.ModelStatus = msg);
        _camera = new CameraDevice();
        _image = new ImageDevice();
        _video = new VideoFileDevice();

        _faceMesh.ImageAvailable += ImageAvailable;

        InitializeComponent();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Runs after the window is fully rendered: loads the placeholder bitmap,
    /// populates the camera combo box, and pre-selects the first available camera.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _placeholderBitmap = LoadPlaceholderBitmap();
        DetectedImage.Source = _placeholderBitmap;
        _model.AvailableCameras = DeviceEnumerator.GetDeviceNames();
        if (_model.AvailableCameras.Count > 0)
            _model.SelectedCameraIndex = 0;
    }

    /// <summary>
    /// Runs when the window is closed: detaches the pipeline, stops all source devices,
    /// and disposes every <see cref="IDisposable"/> pipeline node.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _faceMesh.DetachSource();
        _image.StopSending();
        _video.StopPlayback();
        _camera.StopCamera();
        _faceMesh.Dispose();
        _camera.Dispose();
        _image.Dispose();
        _video.Dispose();
    }

    /// <summary>
    /// Attempts to load <c>Resources/camera_image_placeholder.png</c> from the application
    /// base directory. Returns <see langword="null"/> if the file is not found.
    /// </summary>
    private Bitmap? LoadPlaceholderBitmap()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "camera_image_placeholder.png");
        if (File.Exists(path))
            return new Bitmap(path);
        return null;
    }

    /// <summary>
    /// Called on every processed frame from <see cref="_faceMesh"/>.
    /// Converts the <see cref="OpenCvSharp.Mat"/> to an Avalonia bitmap and updates the
    /// image control on the UI thread, but only while capturing is active.
    /// </summary>
    void ImageAvailable(object? sender, ImageAvailableEventArgs e)
    {
        var bitmap = OpenCvAvaloniaHelper.MatToAvaloniaBitmap(e.Image);
        Dispatcher.UIThread.Post(() =>
        {
            if (_model.IsCapturing)
                DetectedImage.Source = bitmap;
        });
    }

    /// <summary>
    /// Appends <paramref name="message"/> to the log text box on the UI thread
    /// and scrolls the caret to the end.
    /// </summary>
    void LogMessage(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += message + "\n";
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });
    }

    /// <summary>
    /// Handles the Start button. Validates the camera selection, stops any running source,
    /// attaches the camera to the face-mesh pipeline, and starts the capture loop.
    /// </summary>
    void StartCapturing_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_model.AvailableCameras.Count == 0)
        {
            LogMessage("No camera available!");
            return;
        }
        if (_model.SelectedCameraIndex < 0)
        {
            LogMessage("No camera selected!");
            return;
        }

        StopAllSources();

        LogMessage("Capturing started from camera");
        _model.IsCapturing = true;
        _model.ImagePath = "Source: camera";

        StartFaceMeshCapture(_camera);
        _camera.StartCamera(_model.SelectedCameraIndex);
    }

    /// <summary>
    /// Handles the Stop button. Stops all sources, clears the capturing flag, and
    /// restores the placeholder bitmap after a short delay.
    /// </summary>
    void StopCapturing_OnClick(object? sender, RoutedEventArgs e)
    {
        LogMessage("Capturing stopped");
        StopAllSources();
        _model.IsCapturing = false;
        _model.ImagePath = "";
        Task.Run(async () =>
        {
            await Task.Delay(500);
            Dispatcher.UIThread.Post(() => DetectedImage.Source = _placeholderBitmap);
        });
    }

    /// <summary>
    /// Handles the Load Media button. Opens a file picker, then routes the selected file
    /// to either <see cref="_image"/> (static image) or <see cref="_video"/> (video file)
    /// based on the file extension.
    /// </summary>
    async void LoadMedia_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load media (image or video)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images and Videos")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.mp4", "*.avi", "*.mov", "*.mkv", "*.wmv" }
                }
            }
        });

        if (files.Count == 0) return;

        var filePath = files[0].TryGetLocalPath();
        if (filePath == null) return;

        StopAllSources();

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";

        if (isImage)
        {
            StartFaceMeshCapture(_image);
            if (_image.LoadFromFile(filePath))
            {
                _model.IsCapturing = true;
                _model.ImagePath = "Source: " + Path.GetFileName(filePath);
                _image.StartSending();
                LogMessage("Image loaded: " + filePath);
            }
            else
            {
                LogMessage("Error: Failed to load image: " + filePath);
            }
        }
        else
        {
            StartFaceMeshCapture(_video);
            if (_video.LoadFromFile(filePath))
            {
                _model.IsCapturing = true;
                _model.ImagePath = "Source: " + Path.GetFileName(filePath);
                _video.StartPlayback();
                LogMessage("Video loaded: " + filePath);
            }
            else
            {
                LogMessage("Error: Failed to load video: " + filePath);
            }
        }
    }

    /// <summary>
    /// Stops the camera, image, and video source nodes and detaches the face-mesh
    /// processor from all upstream sources.
    /// </summary>
    void StopAllSources()
    {
        _camera.StopCamera();
        _image.StopSending();
        _video.StopPlayback();
        _faceMesh.DetachSource();
    }

    /// <summary>
    /// Attaches <paramref name="source"/> to the face-mesh pipeline and, if the
    /// MediaPipe graph has not been initialised yet, starts <see cref="FaceMeshDevice.InitializeAsync"/>
    /// in the background. Frames received before initialisation completes are forwarded
    /// downstream without landmark annotation.
    /// </summary>
    void StartFaceMeshCapture(ImageProcessor source)
    {
        _model.ModelStatus = "";
        _faceMesh.AttachSource(source);
        if (!_faceMesh.IsInitialized)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _faceMesh.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _model.ModelStatus = "Error: " + ex.Message;
                        LogMessage("Face mesh init error: " + ex.Message);
                    });
                }
            });
        }
    }

    /// <summary>
    /// Toggles the visibility of the advanced settings panel when the checkbox state changes.
    /// </summary>
    void ShowAdvancedSettings_OnChanged(object? sender, RoutedEventArgs e)
    {
        AdvancedSettingsPanel.IsVisible = ShowAdvancedCheckBox.IsChecked == true;
    }
}
