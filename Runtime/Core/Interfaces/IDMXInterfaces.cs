using UnityEngine;
using System.Collections.Generic;

// Core DMX device interface - simplified to only require specific universe and channel
public interface IDMXDevice
{
    int NumChannels { get; }
    int StartChannel { get; set; }
    int Universe { get; set; }
    DMXDeviceMode DeviceMode { get; }
    
    // Separate methods for input and output data
    byte[] GetInputData();
    byte[] GetOutputData();
    void SetInputData(byte[] data);
}

// Communication interface - for the controller
public interface IDMXCommunicator
{
    void SendData(IDMXDevice device);
    void RegisterDevice(IDMXDevice device);
    void UnregisterDevice(IDMXDevice device);
}

// Simple device factory - just for auto-discovery
public static class DMXDeviceDiscovery
{
    public static List<IDMXDevice> DiscoverDevices()
    {
        var devices = new List<IDMXDevice>();
        
        // Find all existing DMX devices
        var existingDevices = Object.FindObjectsOfType<DMXDevice>();
        devices.AddRange(existingDevices);
        
        // Auto-convert Light components to DMX devices
        var lights = Object.FindObjectsOfType<Light>();
        foreach (var light in lights)
        {
            if (light.GetComponent<DMXDevice>() == null)
            {
                var dmxLight = light.gameObject.AddComponent<SimpleDMXLight>();
                devices.Add(dmxLight);
            }
        }
        
        return devices;
    }
}