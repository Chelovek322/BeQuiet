using NAudio.Wave;

namespace BeQuietApplication;

internal class CachedSound
{
    public float[] AudioData { get; private set; }
    public WaveFormat WaveFormat { get; private set; }
    public TimeSpan AudioLength { get; private set; }

    public CachedSound(string audioFileName)
    {
        using var audioFileReader = new AudioFileReader(audioFileName);
        if (audioFileReader.WaveFormat.SampleRate != 44100)
        {
            var resampler = new MediaFoundationResampler(audioFileReader, 44100);
            WaveFormat = resampler.WaveFormat;
        }
        else
        {
            WaveFormat = audioFileReader.WaveFormat;
        }

        AudioLength = audioFileReader.TotalTime;
        var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
        var readBuffer = new float[audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels];
        int samplesRead;
        while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            wholeFile.AddRange(readBuffer.Take(samplesRead));
        }

        AudioData = wholeFile.ToArray();
    }
}