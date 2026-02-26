using System.ComponentModel;

namespace FaceFinderDemo;

/// <summary>
/// View model for <see cref="MainWindow"/>.
/// Exposes all bindable UI state and raises <see cref="PropertyChanged"/> notifications
/// so that Avalonia data-binding updates the view automatically.
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Display names of all cameras discovered at startup, e.g. <c>"Camera 0"</c>, <c>"Camera 1"</c>.
    /// Populates the camera selection combo box.
    /// </summary>
    public List<string> AvailableCameras
    {
        get => _availableCameras;
        set { _availableCameras = value; OnPropertyChanged(nameof(AvailableCameras)); }
    }

    /// <summary>
    /// Zero-based index of the camera selected in the combo box, or <c>-1</c> if none is selected.
    /// </summary>
    public int SelectedCameraIndex
    {
        get => _selectedCameraIndex;
        set { _selectedCameraIndex = value; OnPropertyChanged(nameof(SelectedCameraIndex)); }
    }

    /// <summary>
    /// <see langword="true"/> while the image pipeline is running and frames are being sent
    /// to the display. Controls the enabled state of the Start / Stop buttons.
    /// </summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        set { _isCapturing = value; OnPropertyChanged(nameof(IsCapturing)); }
    }

    /// <summary>
    /// Short human-readable label describing the current media source shown below the image,
    /// e.g. <c>"Source: camera"</c> or <c>"Source: video.mp4"</c>.
    /// </summary>
    public string ImagePath
    {
        get => _imagePath;
        set { _imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
    }

    /// <summary>
    /// Status message from the MediaPipe face-mesh pipeline, such as model download progress
    /// or initialisation errors. Displayed in small text beneath the controls panel.
    /// </summary>
    public string ModelStatus
    {
        get => _modelStatus;
        set { _modelStatus = value; OnPropertyChanged(nameof(ModelStatus)); }
    }

    List<string> _availableCameras = new();
    int _selectedCameraIndex = -1;
    bool _isCapturing;
    string _imagePath = "";
    string _modelStatus = "";

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property name.</summary>
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
