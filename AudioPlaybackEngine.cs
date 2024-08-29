using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BeQuietApplication;

internal class AudioPlaybackEngine : IDisposable
{
    private readonly IWavePlayer _outputDevice;
    private readonly MixingSampleProvider _mixer;
    private const int SampleRate = 44100;
    private const int ChannelCount = 2;

    private AudioPlaybackEngine()
    {
        _outputDevice = new WaveOutEvent();
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, ChannelCount))
        {
            ReadFully = true
        };
        _outputDevice.Init(_mixer);
        _outputDevice.Play();
    }

    public void PlaySound(string fileName)
    {
        var input = new AudioFileReader(fileName);
        AddMixerInput(new AutoDisposeFileReader(input));
    }

    public void PlaySound(CachedSound sound)
    {
        AddMixerInput(new CachedSoundSampleProvider(sound));
    }

    private ISampleProvider ConvertToRightChannelCount(ISampleProvider input)
    {
        if (input.WaveFormat.Channels == _mixer.WaveFormat.Channels)
        {
            return input;
        }

        if (input.WaveFormat.Channels == 1 && _mixer.WaveFormat.Channels == 2)
        {
            return new MonoToStereoSampleProvider(input);
        }

        throw new NotImplementedException("Not yet implemented this channel count conversion");
    }

    private void AddMixerInput(ISampleProvider input)
    {
        _mixer.AddMixerInput(ConvertToRightChannelCount(input));
    }

    public void ChangeVolume(float volume)
    {
        _outputDevice.Volume = volume;
    }

    public void Dispose()
    {
        _outputDevice.Dispose();
    }

    public static readonly AudioPlaybackEngine Instance = new();
}