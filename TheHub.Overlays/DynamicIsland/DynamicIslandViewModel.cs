using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using TheHub.Common.Config;
using TheHub.Core.Monitors;
using TheHub.Core.Native;
using TheHub.Core.Services;

namespace TheHub.Overlays.DynamicIsland;

public class AudioDeviceItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsCapture { get; set; }
    public string Icon { get; set; } = "🔊";

    public override string ToString() => ShortName;
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}

public class DynamicIslandViewModel : INotifyPropertyChanged
{
    private readonly IAudioService _audioService;
    private readonly IBrightnessService _brightnessService;

    private double _masterVolume = 50;
    private string _volumeText = "50%";
    private double _brightness = 100;
    private string _brightnessText = "100%";
    private string _headerTitle = "Audio Live";
    private string _headerSubtitle = "50%";
    private bool _isMasterMuted;
    private bool _isMicMuted;
    private bool _isDeafened;
    private string _micButtonText = "Mic Active";
    private Brush _micButtonForeground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // #00E676
    private string _deafenButtonText = "Deafen";
    private Brush _deafenButtonForeground = new SolidColorBrush(Color.FromRgb(228, 228, 231)); // #E4E4E7
    private AudioDeviceItem? _selectedPlaybackDevice;
    private AudioDeviceItem? _selectedRecordingDevice;
    private bool _isInternalUpdating;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? RequestClose;
    public event Action<string, string, string>? NotificationRequested;

    public ObservableCollection<AudioDeviceItem> PlaybackDevices { get; } = new();
    public ObservableCollection<AudioDeviceItem> RecordingDevices { get; } = new();

    public ICommand CloseCommand { get; }
    public ICommand ToggleMicMuteCommand { get; }
    public ICommand ToggleDeafenCommand { get; }
    public ICommand ToggleMasterMuteCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

    public DynamicIslandViewModel() : this(null, null)
    {
    }

    public DynamicIslandViewModel(IAudioService? audioService = null, IBrightnessService? brightnessService = null)
    {
        _audioService = audioService ?? new AudioService();
        _brightnessService = brightnessService ?? new BrightnessService();

        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        ToggleMicMuteCommand = new RelayCommand(async () => await ToggleMicMuteAsync());
        ToggleDeafenCommand = new RelayCommand(async () => await ToggleDeafenAsync());
        ToggleMasterMuteCommand = new RelayCommand(async () => await ToggleMasterMuteAsync());
        RefreshDevicesCommand = new RelayCommand(async () => await RefreshDevicesAsync());

        _ = InitializeAsync();
    }

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (SetProperty(ref _masterVolume, value))
            {
                int intVal = (int)Math.Round(value);
                VolumeText = $"{intVal}%";
                HeaderSubtitle = $"{intVal}%";
                if (!_isInternalUpdating)
                {
                    _ = _audioService.SetMasterVolumeAsync(intVal);
                }
            }
        }
    }

    public string VolumeText
    {
        get => _volumeText;
        private set => SetProperty(ref _volumeText, value);
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, value))
            {
                int intVal = (int)Math.Round(value);
                BrightnessText = $"{intVal}%";
                if (!_isInternalUpdating)
                {
                    var settings = SettingsManager.Current;
                    var mon = DisplayMonitorHelper.GetTargetMonitor(settings.IslandMonitorIndex);
                    DisplayDimmerOverlay.Instance.SetBrightness(intVal, mon);
                    _ = _brightnessService.SetBrightnessAsync(intVal, settings.IslandMonitorIndex);
                }
            }
        }
    }

    public string BrightnessText
    {
        get => _brightnessText;
        private set => SetProperty(ref _brightnessText, value);
    }

    public string HeaderTitle
    {
        get => _headerTitle;
        set => SetProperty(ref _headerTitle, value);
    }

    public string HeaderSubtitle
    {
        get => _headerSubtitle;
        set => SetProperty(ref _headerSubtitle, value);
    }

    public bool IsMasterMuted
    {
        get => _isMasterMuted;
        set => SetProperty(ref _isMasterMuted, value);
    }

    public bool IsMicMuted
    {
        get => _isMicMuted;
        set
        {
            if (SetProperty(ref _isMicMuted, value))
            {
                MicButtonText = value ? "Mic Inactive" : "Mic Active";
                MicButtonForeground = value
                    ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) // #F87171 Red
                    : new SolidColorBrush(Color.FromRgb(0, 230, 118));  // #00E676 Green
            }
        }
    }

    public string MicButtonText
    {
        get => _micButtonText;
        private set => SetProperty(ref _micButtonText, value);
    }

    public Brush MicButtonForeground
    {
        get => _micButtonForeground;
        private set => SetProperty(ref _micButtonForeground, value);
    }

    public bool IsDeafened
    {
        get => _isDeafened;
        set
        {
            if (SetProperty(ref _isDeafened, value))
            {
                DeafenButtonText = value ? "Deafened" : "Deafen";
                DeafenButtonForeground = value
                    ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) // Red
                    : new SolidColorBrush(Color.FromRgb(228, 228, 231)); // Gray
            }
        }
    }

    public string DeafenButtonText
    {
        get => _deafenButtonText;
        private set => SetProperty(ref _deafenButtonText, value);
    }

    public Brush DeafenButtonForeground
    {
        get => _deafenButtonForeground;
        private set => SetProperty(ref _deafenButtonForeground, value);
    }

    public AudioDeviceItem? SelectedPlaybackDevice
    {
        get => _selectedPlaybackDevice;
        set
        {
            if (SetProperty(ref _selectedPlaybackDevice, value) && value != null && !_isInternalUpdating)
            {
                _ = OnPlaybackDeviceSelectedAsync(value);
            }
        }
    }

    public AudioDeviceItem? SelectedRecordingDevice
    {
        get => _selectedRecordingDevice;
        set
        {
            if (SetProperty(ref _selectedRecordingDevice, value) && value != null && !_isInternalUpdating)
            {
                _ = OnRecordingDeviceSelectedAsync(value);
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _isInternalUpdating = true;

            var (vol, isMuted) = await _audioService.GetMasterVolumeStateAsync();
            MasterVolume = vol;
            IsMasterMuted = isMuted;

            int brightness = await _brightnessService.GetBrightnessAsync();
            Brightness = brightness;

            bool micMuted = await _audioService.GetMicrophoneMuteStateAsync();
            IsMicMuted = micMuted;

            await RefreshDevicesAsync();
        }
        finally
        {
            _isInternalUpdating = false;
        }
    }

    public async Task RefreshDevicesAsync()
    {
        try
        {
            var playback = await _audioService.GetPlaybackDevicesAsync();
            var recording = await _audioService.GetRecordingDevicesAsync();

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                _isInternalUpdating = true;
                try
                {
                    PlaybackDevices.Clear();
                    foreach (var dev in playback)
                    {
                        string icon = dev.Name.Contains("Headphone", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ? "🎧" : "🔊";
                        string clean = dev.Name.Length > 22 ? dev.Name[..21] + "…" : dev.Name;
                        PlaybackDevices.Add(new AudioDeviceItem
                        {
                            Id = dev.Id,
                            Name = dev.Name,
                            ShortName = clean,
                            IsDefault = dev.IsDefault,
                            IsCapture = false,
                            Icon = icon
                        });
                    }

                    RecordingDevices.Clear();
                    foreach (var dev in recording)
                    {
                        string clean = dev.Name.Length > 22 ? dev.Name[..21] + "…" : dev.Name;
                        RecordingDevices.Add(new AudioDeviceItem
                        {
                            Id = dev.Id,
                            Name = dev.Name,
                            ShortName = clean,
                            IsDefault = dev.IsDefault,
                            IsCapture = true,
                            Icon = "🎙️"
                        });
                    }

                    _selectedPlaybackDevice = PlaybackDevices.FirstOrDefault(d => d.IsDefault) ?? PlaybackDevices.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedPlaybackDevice));

                    _selectedRecordingDevice = RecordingDevices.FirstOrDefault(d => d.IsDefault) ?? RecordingDevices.FirstOrDefault();
                    OnPropertyChanged(nameof(SelectedRecordingDevice));
                }
                finally
                {
                    _isInternalUpdating = false;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIslandViewModel] RefreshDevicesAsync error: {ex.Message}");
        }
    }

    private async Task OnPlaybackDeviceSelectedAsync(AudioDeviceItem device)
    {
        bool success = await _audioService.SetDefaultDeviceAsync(device.Id, isCapture: false);
        if (success)
        {
            NotificationRequested?.Invoke("Audio Output Changed", device.Name, device.Icon);
        }
    }

    private async Task OnRecordingDeviceSelectedAsync(AudioDeviceItem device)
    {
        bool success = await _audioService.SetDefaultDeviceAsync(device.Id, isCapture: true);
        if (success)
        {
            NotificationRequested?.Invoke("Microphone Changed", device.Name, "🎙️");
        }
    }

    public async Task ToggleMicMuteAsync()
    {
        bool newMute = await _audioService.ToggleMicrophoneMuteAsync();
        IsMicMuted = newMute;
        NotificationRequested?.Invoke(newMute ? "Microphone Muted" : "Microphone Active", newMute ? "Voice input muted" : "Voice input live", newMute ? "🎙️" : "🎤");
    }

    public async Task ToggleDeafenAsync()
    {
        bool nextDeafen = !IsDeafened;
        IsDeafened = nextDeafen;
        var (masterMuted, micMuted) = await _audioService.SetDeafenAsync(nextDeafen);
        IsMasterMuted = masterMuted;
        IsMicMuted = micMuted;

        var (vol, _) = await _audioService.GetMasterVolumeStateAsync();
        if (!nextDeafen && vol == 0)
        {
            await _audioService.SetMasterVolumeAsync(50);
            vol = 50;
        }

        NotificationRequested?.Invoke(
            nextDeafen ? "Audio Deafened" : "Audio Undeafened",
            nextDeafen ? "Speakers & mic muted" : $"{vol}% (Restored)",
            nextDeafen ? "🔇" : (vol > 65 ? "🔊" : (vol > 30 ? "🔉" : "🔈")));
    }

    public async Task ToggleMasterMuteAsync()
    {
        var (vol, isMuted) = await _audioService.ToggleMasterMuteAsync();
        _isInternalUpdating = true;
        MasterVolume = vol;
        IsMasterMuted = isMuted;
        _isInternalUpdating = false;

        NotificationRequested?.Invoke(
            isMuted ? "Audio Muted" : "Audio Unmuted",
            isMuted ? "Speakers muted" : $"{vol}%",
            isMuted ? "🔇" : "🔊");
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
