using UnityEngine;

// Core DMX device interface
public interface IDMXDevice
{
    int NumChannels { get; }
    int StartChannel { get; set; }
    DMXDeviceMode DeviceMode { get; }
    byte[] GetDMXData();
    void SetDMXData(byte[] data);
}

// Command execution interface
public interface IDMXCommand
{
    void Execute();
    void Undo();
}

// Channel processing interface
public interface IDMXChannelProcessor
{
    void ProcessChannel(ChannelFunction function, byte value, byte fineValue = 0);
    byte ReadChannel(ChannelFunction function);
}

// Data provider interface for reading from Unity components
public interface IDMXDataProvider
{
    byte[] ReadData();
}

// Data consumer interface for applying to Unity components  
public interface IDMXDataConsumer
{
    void ApplyData(byte[] data);
}

// Communication interface
public interface IDMXCommunicator
{
    void SendData(IDMXDevice device);
    void RegisterDevice(IDMXDevice device);
    void UnregisterDevice(IDMXDevice device);
}

// Channel layout interface for auto-assignment strategies
public interface IChannelLayoutStrategy
{
    void AssignChannels(DmxController.UniverseDevices universe);
    bool CanFitDevice(DmxController.UniverseDevices universe, IDMXDevice device);
    string GetLayoutName();
}