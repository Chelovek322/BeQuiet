using NAudio.Wave;

namespace BeQuietApplication;

internal class CachedSoundSampleProvider(CachedSound cachedSound) : ISampleProvider
{
    public WaveFormat WaveFormat => cachedSound.WaveFormat;

    private long _position;

    public int Read(float[] buffer, int offset, int count)
    {
        var availableSamples = cachedSound.AudioData.Length - _position;
        var samplesToCopy = Math.Min(availableSamples, count);
        Array.Copy(cachedSound.AudioData, _position, buffer, offset, samplesToCopy);
        _position += samplesToCopy;
        return (int)samplesToCopy;
    }
}