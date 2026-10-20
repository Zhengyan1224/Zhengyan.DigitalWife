using Zhengyan.DigitalWife.Audio;

namespace Zhengyan.DigitalWife.GamePlayer.Android;

internal static class AndroidVoiceCapture
{
    internal static async Task<AudioData> RecordAsync(IAudioSource source, TimeSpan duration,
        AudioCaptureOptions options, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        List<float> samples = [];
        int target = checked((int)(duration.TotalSeconds * options.SampleRate * options.Channels));
        await foreach (AudioChunk chunk in source.CaptureAsync(options, cancellationToken).ConfigureAwait(false))
        {
            int count = Math.Min(chunk.Samples.Length, target - samples.Count);
            samples.AddRange(chunk.Samples.Slice(0, count).ToArray());
            if (samples.Count >= target) break;
        }
        return new(samples.ToArray(), new(options.SampleRate, options.Channels));
    }

    internal static async Task<AudioData> RecordUntilSilenceAsync(IAudioSource source,
        VoiceActivityCaptureOptions options, CancellationToken cancellationToken)
    {
        Queue<float> preRoll = new();
        List<float> samples = [];
        int rate = checked(options.SampleRate * options.Channels);
        int preRollCount = Math.Max(0, (int)(options.PreRoll.TotalSeconds * rate));
        int minimum = Math.Max(0, (int)(options.MinDuration.TotalSeconds * rate));
        int maximum = Math.Max(1, (int)(options.MaxDuration.TotalSeconds * rate));
        int silenceLimit = Math.Max(0, (int)(options.SilenceTimeout.TotalSeconds * rate));
        int silenceCount = 0;
        bool started = false;
        await foreach (AudioChunk chunk in source.CaptureAsync(options, cancellationToken).ConfigureAwait(false))
        {
            float[] buffer = chunk.Samples.ToArray();
            double squareSum = 0;
            foreach (float sample in buffer) squareSum += sample * sample;
            bool speech = buffer.Length > 0 && Math.Sqrt(squareSum / buffer.Length) >= options.SilenceThreshold;
            if (!started)
            {
                if (!speech)
                {
                    foreach (float sample in buffer)
                    {
                        preRoll.Enqueue(sample);
                        if (preRoll.Count > preRollCount) preRoll.Dequeue();
                    }
                    continue;
                }
                started = true;
                samples.AddRange(preRoll);
            }
            samples.AddRange(buffer.Take(Math.Max(0, maximum - samples.Count)));
            silenceCount = speech ? 0 : silenceCount + buffer.Length;
            if (samples.Count >= maximum || (samples.Count >= minimum && silenceCount >= silenceLimit)) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(samples.ToArray(), new(options.SampleRate, options.Channels));
    }
}
