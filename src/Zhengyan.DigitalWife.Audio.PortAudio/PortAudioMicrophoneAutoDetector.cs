using Microsoft.Extensions.Logging;
using PortAudioSharp;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

public sealed class PortAudioMicrophoneAutoDetector
{
    private static readonly int[] FallbackSampleRates = [16_000, 24_000, 44_100, 48_000];

    private readonly ILogger _logger;

    public PortAudioMicrophoneAutoDetector(ILogger logger)
    {
        _logger = logger;
    }

    public PortAudioMicrophoneDetectionResult Detect(PortAudioMicrophoneDetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sampleRates = BuildSampleRates(options.PreferredSampleRates);
        var candidates = new List<PortAudioMicrophoneDetectionCandidate>();

        PortAudioSharp.PortAudio.LoadNativeLibrary();
        PortAudioSharp.PortAudio.Initialize();

        try
        {
            int defaultInputDevice = PortAudioSharp.PortAudio.DefaultInputDevice;
            var deviceIndices = BuildDeviceOrder(defaultInputDevice, options.PreferredDeviceIndices);
            if (deviceIndices.Count == 0)
            {
                return PortAudioMicrophoneDetectionResult.NotDetected("No PortAudio input devices found.", candidates);
            }

            foreach (int deviceIndex in deviceIndices)
            {
                DeviceInfo deviceInfo;
                try
                {
                    deviceInfo = PortAudioSharp.PortAudio.GetDeviceInfo(deviceIndex);
                }
                catch (Exception ex)
                {
                    candidates.Add(new PortAudioMicrophoneDetectionCandidate(
                        deviceIndex,
                        $"Device {deviceIndex}",
                        0,
                        0.0,
                        IsDefault: false,
                        IsUsable: false,
                        SampleRate: null,
                        Rms: 0.0,
                        Error: ex.Message));
                    continue;
                }

                if (deviceInfo.maxInputChannels <= 0)
                {
                    continue;
                }

                foreach (int sampleRate in sampleRates)
                {
                    PortAudioMicrophoneProbeResult probe = ProbeDevice(
                        deviceIndex,
                        deviceInfo,
                        sampleRate,
                        options.Channels,
                        options.FramesPerBuffer,
                        options.ProbeDuration);

                    candidates.Add(new PortAudioMicrophoneDetectionCandidate(
                        deviceIndex,
                        deviceInfo.name,
                        deviceInfo.maxInputChannels,
                        deviceInfo.defaultSampleRate,
                        deviceIndex == defaultInputDevice,
                        probe.IsUsable,
                        sampleRate,
                        probe.Rms,
                        probe.Error));

                    if (probe.IsUsable && (!options.RequireSignal || probe.Rms >= options.MinRms))
                    {
                        _logger.LogInformation(
                            "Auto-detected microphone [{DeviceIndex}] {DeviceName}, sampleRate={SampleRate}, rms={Rms:0.000000}.",
                            deviceIndex,
                            deviceInfo.name,
                            sampleRate,
                            probe.Rms);

                        return PortAudioMicrophoneDetectionResult.Detected(
                            deviceIndex,
                            deviceInfo.name,
                            sampleRate,
                            probe.Rms,
                            deviceIndex == defaultInputDevice,
                            candidates);
                    }
                }
            }

            PortAudioMicrophoneDetectionCandidate? firstUsable = candidates.FirstOrDefault(candidate => candidate.IsUsable);
            if (firstUsable is not null)
            {
                return PortAudioMicrophoneDetectionResult.Detected(
                    firstUsable.DeviceIndex,
                    firstUsable.Name,
                    firstUsable.SampleRate!.Value,
                    firstUsable.Rms,
                    firstUsable.IsDefault,
                    candidates);
            }

            return PortAudioMicrophoneDetectionResult.NotDetected("No usable PortAudio input device was found.", candidates);
        }
        finally
        {
            PortAudioSharp.PortAudio.Terminate();
        }
    }

    private static IReadOnlyList<int> BuildSampleRates(IReadOnlyList<int> preferredSampleRates)
    {
        var sampleRates = new List<int>();
        foreach (int sampleRate in preferredSampleRates.Concat(FallbackSampleRates))
        {
            if (sampleRate >= 8_000 && sampleRate <= 192_000 && !sampleRates.Contains(sampleRate))
            {
                sampleRates.Add(sampleRate);
            }
        }

        return sampleRates.Count == 0 ? FallbackSampleRates : sampleRates;
    }

    private static IReadOnlyList<int> BuildDeviceOrder(int defaultInputDevice, IReadOnlyList<int> preferredDeviceIndices)
    {
        var deviceIndices = new List<int>();

        foreach (int index in preferredDeviceIndices)
        {
            AddDeviceIndex(index);
        }

        AddDeviceIndex(defaultInputDevice);

        for (var index = 0; index < PortAudioSharp.PortAudio.DeviceCount; index++)
        {
            AddDeviceIndex(index);
        }

        return deviceIndices;

        void AddDeviceIndex(int index)
        {
            if (index >= 0 && !deviceIndices.Contains(index))
            {
                deviceIndices.Add(index);
            }
        }
    }

    private PortAudioMicrophoneProbeResult ProbeDevice(
        int deviceIndex,
        DeviceInfo deviceInfo,
        int sampleRate,
        int channels,
        uint framesPerBuffer,
        TimeSpan duration)
    {
        channels = Math.Clamp(channels, 1, Math.Max(1, deviceInfo.maxInputChannels));
        framesPerBuffer = framesPerBuffer == 0 ? 512u : Math.Clamp(framesPerBuffer, 64u, 8192u);
        duration = duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(350) : duration;

        PortAudioSharp.Stream? stream = null;
        double sumSquares = 0.0;
        long sampleCount = 0;
        long targetSamples = Math.Max(1, (long)Math.Round(duration.TotalSeconds * sampleRate * channels));
        Exception? callbackError = null;

        try
        {
            var input = new StreamParameters
            {
                device = deviceIndex,
                channelCount = channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = deviceInfo.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            stream = new PortAudioSharp.Stream(
                input,
                null,
                sampleRate,
                framesPerBuffer,
                StreamFlags.NoFlag,
                (IntPtr inputPtr, IntPtr outputPtr, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, IntPtr userData) =>
                {
                    try
                    {
                        var count = checked((int)frameCount * channels);
                        var buffer = new float[count];
                        System.Runtime.InteropServices.Marshal.Copy(inputPtr, buffer, 0, count);
                        foreach (float sample in buffer)
                        {
                            sumSquares += sample * sample;
                        }

                        sampleCount += count;
                        return sampleCount >= targetSamples
                            ? StreamCallbackResult.Complete
                            : StreamCallbackResult.Continue;
                    }
                    catch (Exception ex)
                    {
                        callbackError = ex;
                        return StreamCallbackResult.Abort;
                    }
                },
                null!);

            stream.Start();
            DateTimeOffset deadline = DateTimeOffset.UtcNow + duration + TimeSpan.FromMilliseconds(800);
            while (sampleCount < targetSamples && DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            if (callbackError is not null)
            {
                return PortAudioMicrophoneProbeResult.Failure(callbackError.Message);
            }

            double rms = sampleCount <= 0 ? 0.0 : Math.Sqrt(sumSquares / sampleCount);
            return PortAudioMicrophoneProbeResult.Success(rms);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Microphone probe failed for device {DeviceIndex} at {SampleRate} Hz.", deviceIndex, sampleRate);
            return PortAudioMicrophoneProbeResult.Failure(ex.Message);
        }
        finally
        {
            try
            {
                stream?.Stop();
            }
            catch
            {
            }

            try
            {
                stream?.Close();
            }
            catch
            {
            }

            stream?.Dispose();
        }
    }

    private readonly record struct PortAudioMicrophoneProbeResult(bool IsUsable, double Rms, string Error)
    {
        public static PortAudioMicrophoneProbeResult Success(double rms) => new(true, rms, string.Empty);

        public static PortAudioMicrophoneProbeResult Failure(string error) => new(false, 0.0, error);
    }
}

public sealed class PortAudioMicrophoneDetectionOptions
{
    public IReadOnlyList<int> PreferredDeviceIndices { get; init; } = [];

    public IReadOnlyList<int> PreferredSampleRates { get; init; } = [];

    public int Channels { get; init; } = 1;

    public uint FramesPerBuffer { get; init; } = 512;

    public TimeSpan ProbeDuration { get; init; } = TimeSpan.FromMilliseconds(350);

    public double MinRms { get; init; } = 0.0001;

    public bool RequireSignal { get; init; }
}

public sealed record PortAudioMicrophoneDetectionResult(
    bool Success,
    int? DeviceIndex,
    string DeviceName,
    int? SampleRate,
    double Rms,
    bool IsDefaultDevice,
    string Error,
    IReadOnlyList<PortAudioMicrophoneDetectionCandidate> Candidates)
{
    public static PortAudioMicrophoneDetectionResult Detected(
        int deviceIndex,
        string deviceName,
        int sampleRate,
        double rms,
        bool isDefaultDevice,
        IReadOnlyList<PortAudioMicrophoneDetectionCandidate> candidates)
        => new(true, deviceIndex, deviceName, sampleRate, rms, isDefaultDevice, string.Empty, candidates.ToArray());

    public static PortAudioMicrophoneDetectionResult NotDetected(
        string error,
        IReadOnlyList<PortAudioMicrophoneDetectionCandidate> candidates)
        => new(false, null, string.Empty, null, 0.0, false, error, candidates.ToArray());
}

public sealed record PortAudioMicrophoneDetectionCandidate(
    int DeviceIndex,
    string Name,
    int MaxInputChannels,
    double DefaultSampleRate,
    bool IsDefault,
    bool IsUsable,
    int? SampleRate,
    double Rms,
    string Error);
