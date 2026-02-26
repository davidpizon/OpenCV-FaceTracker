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

public partial class MainWindow : Avalonia.Controls.Window
{
    FaceMeshDevice _faceMesh;
    MainWindowViewModel _model;
    CameraDevice _camera;
    ImageDevice _image;
    VideoFileDevice _video;
    Bitmap? _placeholderBitmap;

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

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _placeholderBitmap = LoadPlaceholderBitmap();
        DetectedImage.Source = _placeholderBitmap;
        _model.AvailableCameras = DeviceEnumerator.GetDeviceNames();
        if (_model.AvailableCameras.Count > 0)
            _model.SelectedCameraIndex = 0;
    }

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

    private Bitmap? LoadPlaceholderBitmap()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "camera_image_placeholder.png");
        if (File.Exists(path))
            return new Bitmap(path);
        return null;
    }

    void ImageAvailable(object? sender, ImageAvailableEventArgs e)
    {
        var bitmap = OpenCvAvaloniaHelper.MatToAvaloniaBitmap(e.Image);
        Dispatcher.UIThread.Post(() =>
        {
            if (_model.IsCapturing)
                DetectedImage.Source = bitmap;
        });
    }

    void LogMessage(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += message + "\n";
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        });
    }

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

    void StopAllSources()
    {
        _camera.StopCamera();
        _image.StopSending();
        _video.StopPlayback();
        _faceMesh.DetachSource();
    }

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

    void ShowAdvancedSettings_OnChanged(object? sender, RoutedEventArgs e)
    {
        AdvancedSettingsPanel.IsVisible = ShowAdvancedCheckBox.IsChecked == true;
    }
}
