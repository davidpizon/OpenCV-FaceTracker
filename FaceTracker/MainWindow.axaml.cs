using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    FaceDetectorDevice _faceDetection;
    MainWindowViewModel _model;
    CameraDevice _camera;
    ImageDevice _image;
    VideoFileDevice _video;
    Bitmap? _placeholderBitmap;

    public MainWindow()
    {
        _model = new MainWindowViewModel();
        DataContext = _model;

        _faceDetection = new FaceDetectorDevice();
        _camera = new CameraDevice();
        _image = new ImageDevice();
        _video = new VideoFileDevice();

        _faceDetection.ImageAvailable += ImageAvailable;
        _faceDetection.FaceDetectorStateChanged += FaceDetectorStateChanged;
        _model.SelectedDetectionMode = FaceDetectorDevice.DetectionModes.Periodic;
        _model.DrawDetection = true;
        _model.DetectionPeriod = 500;
        _model.LastDetection = "None";

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
        _camera.StopCamera();
        _image.StopSending();
        _video.StopPlayback();
        _faceDetection.Dispose();
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

    void FaceDetectorStateChanged(object? sender, FaceDetectionEventArgs e)
    {
        var message = new StringBuilder();
        _model.CurrentlyDetecting = e.Starting;
        if (!e.Starting)
        {
            message.AppendFormat("Detection took {0} ms, ", e.DetectionTime);
            if (e.Faces == null || e.Faces.Count == 0)
            {
                _model.LastDetection = "None";
                message.Append("no face found");
            }
            else if (e.Faces.Count == 1)
            {
                _model.LastDetection = e.Faces[0].IsValid ? "Full face" : "Partial face";
                message.AppendFormat("one {0} face found", e.Faces[0].IsValid ? "full" : "partial");
            }
            else
            {
                bool allValid = e.Faces.All(f => f.IsValid);
                _model.LastDetection = allValid ? e.Faces.Count + " full faces" : e.Faces.Count + " partial faces";
                message.AppendFormat("{0} {1} faces found", e.Faces.Count, allValid ? "full" : "partial");
            }
        }
        else
        {
            message.Append("Starting detection");
        }
        LogMessage(message.ToString());
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
        _faceDetection.DetectionMode = _model.SelectedDetectionMode;
        _faceDetection.DrawDetection = _model.DrawDetection;
        _faceDetection.DrawProbableAreas = _model.DrawProbableAreas;
        _faceDetection.AttachSource(_camera);
        _faceDetection.ResetDetections();
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
            _faceDetection.AttachSource(_image);
            if (_image.LoadFromFile(filePath))
            {
                _model.IsCapturing = true;
                _model.ImagePath = "Source: " + Path.GetFileName(filePath);
                _faceDetection.DetectionMode = _model.SelectedDetectionMode;
                _faceDetection.DrawDetection = _model.DrawDetection;
                _faceDetection.DrawProbableAreas = _model.DrawProbableAreas;
                _faceDetection.ResetDetections();
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
            _faceDetection.AttachSource(_video);
            if (_video.LoadFromFile(filePath))
            {
                _model.IsCapturing = true;
                _model.ImagePath = "Source: " + Path.GetFileName(filePath);
                _faceDetection.DetectionMode = _model.SelectedDetectionMode;
                _faceDetection.DrawDetection = _model.DrawDetection;
                _faceDetection.DrawProbableAreas = _model.DrawProbableAreas;
                _faceDetection.ResetDetections();
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
        _faceDetection.DetachSource();
    }

    void DetectionMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _faceDetection.DetectionMode = _model.SelectedDetectionMode;
        _model.UpdateState();
    }

    void DrawDetectionCheckboxChanged(object? sender, RoutedEventArgs e)
    {
        _faceDetection.DrawDetection = _model.DrawDetection;
        _faceDetection.DrawProbableAreas = _model.DrawProbableAreas;
    }

    void DetectFace_OnClick(object? sender, RoutedEventArgs e)
    {
        _faceDetection.ManualDetect();
    }

    void DetectionPeriod_OnValueChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value" && sender is Slider slider)
        {
            _faceDetection.DetectionPeriod = TimeSpan.FromMilliseconds(slider.Value);
        }
    }

    void ShowAdvancedSettings_OnChanged(object? sender, RoutedEventArgs e)
    {
        AdvancedSettingsPanel.IsVisible = ShowAdvancedCheckBox.IsChecked == true;
    }
}
