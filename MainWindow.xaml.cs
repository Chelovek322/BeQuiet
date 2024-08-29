using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;

namespace BeQuietApplication;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string SettingsPath = ".\\settings.json";
    private static ApplicationSettings _applicationSettings = null!;
    private static CachedSound? _cachedSound;
    private static bool _isNotificationSoundEnabled = true;
    private static DateTime _lastSoundTime = DateTime.MinValue;
    private static WaveInEvent _waveInEvent = null!;
    private static Dictionary<string, MMDevice> _inputAudioDevices = null!;

    public MainWindow()
    {
#if DEBUG
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
#endif

        if (!File.Exists(SettingsPath))
            File.WriteAllText(SettingsPath, string.Empty);

        var json = File.ReadAllText(SettingsPath);
        if (string.IsNullOrEmpty(json))
        {
            _applicationSettings = new ApplicationSettings();
            SaveSettings();
        }
        else
        {
            _applicationSettings = JsonConvert.DeserializeObject<ApplicationSettings>(json)!;
        }

        if (!string.IsNullOrEmpty(_applicationSettings.AudioFilePath))
        {
            _cachedSound = new CachedSound(_applicationSettings.AudioFilePath);
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        InitializeComponent();

        SoundVolumeSlider.Value = _applicationSettings.SoundVolume;

        ThresholdSlider.Value = _applicationSettings.ThresholdValue;

        if (string.IsNullOrEmpty(_applicationSettings.AudioFilePath))
        {
            SoundIndicatorTextValue.Text = "Звук для воспроизведения не выбран.";
        }


        var enumerator = new MMDeviceEnumerator();
        var defaultInputDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        if (string.IsNullOrEmpty(_applicationSettings.InputDeviceId))
        {
            _applicationSettings.InputDeviceId = defaultInputDevice.ID;
            SaveSettings();
        }

        _inputAudioDevices = GetInputAudioDevices();
        foreach (var (name, device) in _inputAudioDevices)
        {
            InputDeviceComboBox.Items.Add(name);

            if (device.ID == _applicationSettings.InputDeviceId)
                InputDeviceComboBox.SelectedItem = name;
        }

        enumerator.Dispose();

        _waveInEvent = new WaveInEvent();
        _waveInEvent.DataAvailable += OnDataAvailable;
        _waveInEvent.RecordingStopped += (_, _) =>
        {
            _waveInEvent.DeviceNumber = InputDeviceComboBox.SelectedIndex;
            _waveInEvent.StartRecording();
        };
        _waveInEvent.StartRecording();
    }

    private Dictionary<string, MMDevice> GetInputAudioDevices()
    {
        var retVal = new Dictionary<string, MMDevice>();
        var enumerator = new MMDeviceEnumerator();
        var waveInDevices = WaveIn.DeviceCount;
        for (var waveInDevice = 0; waveInDevice < waveInDevices; waveInDevice++)
        {
            var deviceInfo = WaveIn.GetCapabilities(waveInDevice);
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All))
            {
                if (device.FriendlyName.StartsWith(deviceInfo.ProductName))
                {
                    retVal.Add(device.FriendlyName, device);
                    break;
                }
            }
        }

        enumerator.Dispose();

        return retVal;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var e = (Exception)args.ExceptionObject;
        MessageBox.Show($"Произошла ошибка при выполнении программы:\n{e}", "Ошибка", MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (string.IsNullOrEmpty(_applicationSettings.AudioFilePath) || _cachedSound is null)
            return;

        var max = 0f;
        for (var index = 0; index < e.BytesRecorded; index += 2)
        {
            var sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index + 0]);
            var sample32 = sample / 32768f;

            if (sample32 < 0) sample32 = -sample32;
            if (sample32 > max) max = sample32;
        }

        var maxDb = 20 * (float)Math.Log10(max);
        Dispatcher.Invoke(() =>
        {
            SoundIndicator.Value = maxDb;
            SoundIndicatorTextValue.Text = $"{maxDb:0}dB";
        });

        if (_isNotificationSoundEnabled && maxDb >= _applicationSettings.ThresholdValue &&
            _lastSoundTime + _cachedSound.AudioLength < DateTime.UtcNow)
        {
            PlaySound();
            _lastSoundTime = DateTime.UtcNow;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;

        Hide();
        NotifyIcon.ShowBalloonTip("Приложение скрыто",
            "Нажмите на иконку в трее, чтобы раскрыть приложение.",
            BalloonIcon.Info);

        base.OnClosing(e);
    }

    private void OnClickedExit(object _, RoutedEventArgs _1)
    {
        Environment.Exit(0);
    }

    private void NotifyIcon_OnTrayLeftMouseUp(object sender, RoutedEventArgs e)
    {
        Show();
        Activate();
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Open Audio File",
            Filter = "Audio Files (*.mp3;*.wav;*.aac;*.flac)|*.mp3;*.wav;*.aac;*.flac",
            FilterIndex = 1,
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() != true) return;

        _applicationSettings.AudioFilePath = openFileDialog.FileName;
        _cachedSound = new CachedSound(_applicationSettings.AudioFilePath);
        SaveSettings();
    }

    private static void SaveSettings()
    {
        var json = JsonConvert.SerializeObject(_applicationSettings, Formatting.Indented);
        File.WriteAllText(SettingsPath, json);
    }

    private void CheckSound_OnClick(object sender, RoutedEventArgs e)
    {
        PlaySound();
    }

    private static void PlaySound()
    {
        if (_cachedSound is null)
        {
            MessageBox.Show("Не выбран звук для воспроизведения", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AudioPlaybackEngine.Instance.PlaySound(_cachedSound);
    }

    private void SoundVolumeSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        var newVolume = Convert.ToSingle(SoundVolumeSlider.Value / 100);
        AudioPlaybackEngine.Instance.ChangeVolume(newVolume);
        _applicationSettings.SoundVolume = SoundVolumeSlider.Value;
        SaveSettings();
    }

    private void ThresholdSlider_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _applicationSettings.ThresholdValue = Convert.ToInt32(ThresholdSlider.Value);
        SaveSettings();
    }

    private void ToggleNotificationSound_OnClick(object sender, RoutedEventArgs e)
    {
        _isNotificationSoundEnabled = !_isNotificationSoundEnabled;
        Dispatcher.Invoke(() =>
        {
            ToggleNotificationSoundButton.Content = _isNotificationSoundEnabled
                ? "Отключить звук"
                : "Включить звук";
        });
    }

    private void InputDeviceComboBox_OnDropDownClosed(object? sender, EventArgs e)
    {
        if (_waveInEvent.DeviceNumber == InputDeviceComboBox.SelectedIndex)
            return;

        _waveInEvent.StopRecording();

        foreach (var (name, device) in _inputAudioDevices)
        {
            if (name == (string)InputDeviceComboBox.SelectedValue)
            {
                _applicationSettings.InputDeviceId = device.ID;
                SaveSettings();
                break;
            }
        }
    }
}