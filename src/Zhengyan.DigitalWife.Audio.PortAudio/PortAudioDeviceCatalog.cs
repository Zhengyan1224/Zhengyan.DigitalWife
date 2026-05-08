using PortAudioSharp;

namespace Zhengyan.DigitalWife.Audio.PortAudio;

public sealed class PortAudioDeviceCatalog
{
    public IReadOnlyList<PortAudioDeviceDescriptor> ListInputDevices()
        => ListDevices(info => info.maxInputChannels > 0);

    public IReadOnlyList<PortAudioDeviceDescriptor> ListOutputDevices()
        => ListDevices(info => info.maxOutputChannels > 0);

    private static IReadOnlyList<PortAudioDeviceDescriptor> ListDevices(Func<DeviceInfo, bool> predicate)
    {
        PortAudioSharp.PortAudio.LoadNativeLibrary();
        PortAudioSharp.PortAudio.Initialize();

        try
        {
            var devices = new List<PortAudioDeviceDescriptor>();
            for (var index = 0; index < PortAudioSharp.PortAudio.DeviceCount; index++)
            {
                var info = PortAudioSharp.PortAudio.GetDeviceInfo(index);
                if (!predicate(info))
                {
                    continue;
                }

                devices.Add(new PortAudioDeviceDescriptor(
                    index,
                    info.name,
                    info.maxInputChannels,
                    info.maxOutputChannels,
                    info.defaultSampleRate));
            }

            return devices;
        }
        finally
        {
            PortAudioSharp.PortAudio.Terminate();
        }
    }
}

public sealed record PortAudioDeviceDescriptor(
    int Index,
    string Name,
    int MaxInputChannels,
    int MaxOutputChannels,
    double DefaultSampleRate);

