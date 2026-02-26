using System.ComponentModel;

namespace FaceFinderDemo;

public class MainWindowViewModel : INotifyPropertyChanged
{
    public List<string> AvailableCameras
    {
        get => _availableCameras;
        set { _availableCameras = value; OnPropertyChanged(nameof(AvailableCameras)); }
    }

    public int SelectedCameraIndex
    {
        get => _selectedCameraIndex;
        set { _selectedCameraIndex = value; OnPropertyChanged(nameof(SelectedCameraIndex)); }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set { _isCapturing = value; OnPropertyChanged(nameof(IsCapturing)); }
    }

    public string ImagePath
    {
        get => _imagePath;
        set { _imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
