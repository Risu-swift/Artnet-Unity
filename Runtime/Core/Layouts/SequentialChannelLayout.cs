using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SequentialChannelLayout : IChannelLayoutStrategy
{
    public string layoutName = "Sequential";
    public int channelsPerDevice = 4;
    
    public void AssignChannels(DmxController.UniverseDevices universe)
    {
        // Use the existing Initialize method
        universe.Initialize();
    }
    
    public bool CanFitDevice(DmxController.UniverseDevices universe, IDMXDevice device)
    {
        if (universe.devices == null) return true;
        
        // Calculate current channel usage
        int currentChannelCount = 0;
        foreach (var existingDevice in universe.devices)
        {
            if (existingDevice != null)
            {
                currentChannelCount = Mathf.Max(currentChannelCount, 
                    existingDevice.StartChannel + existingDevice.NumChannels);
            }
        }
        
        return currentChannelCount + device.NumChannels <= 512;
    }
    
    public string GetLayoutName()
    {
        return layoutName;
    }
}