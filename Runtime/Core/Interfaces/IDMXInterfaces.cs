using UnityEngine;
using System.Collections.Generic;

// Device placement preferences - devices can specify where they want to be
[System.Serializable]
public struct DMXPlacement
{
    public int universe;
    public int startChannel;
    public bool autoAssign;         // Let controller auto-assign if preferred location is taken
    public int priority;            // Higher priority devices get preference in conflicts
    
    public static DMXPlacement Auto => new DMXPlacement { universe = 0, startChannel = 0, autoAssign = true, priority = 0 };
    public static DMXPlacement Manual(int universe, int startChannel, bool autoAssign = false, int priority = 0) => 
        new DMXPlacement { universe = universe, startChannel = startChannel, autoAssign = autoAssign, priority = priority };
}

// Core DMX device interface - updated with placement preferences
public interface IDMXDevice
{
    int NumChannels { get; }
    int StartChannel { get; set; }
    DMXDeviceMode DeviceMode { get; }
    
    // Device tells controller where it wants to be placed
    DMXPlacement GetPreferredPlacement();
    void OnPlacementAssigned(int universe, int startChannel);
    
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
    
    // New method for device placement queries
    bool IsPlacementAvailable(int universe, int startChannel, int numChannels);
    DMXPlacement SuggestPlacement(IDMXDevice device);
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