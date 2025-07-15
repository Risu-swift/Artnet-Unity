using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SequentialChannelLayout : IChannelLayoutStrategy
{
    public string layoutName = "Sequential";
    public int channelsPerDevice = 4;
    
    public void AssignChannels(DmxController.UniverseDevices universe)
    {
        if (universe.devices == null) return;
        
        int currentChannel = 1; // Start from channel 1
        
        for (int i = 0; i < universe.devices.Length; i++)
        {
            var device = universe.devices[i];
            if (device == null) continue;
            
            device.StartChannel = currentChannel;
            device.name = FormatDeviceName(device, universe, i);
            
            currentChannel += device.NumChannels;
        }
    }
    
    private string FormatDeviceName(DMXDevice device, DmxController.UniverseDevices universe, int deviceIndex)
    {
        int endChannel = device.StartChannel + device.NumChannels - 1;
        
        return string.Format("{0} | U{1} | Ch{2:000}-{3:000} | {4} | D{5}", 
            device.GetType().Name,
            universe.universe, 
            device.StartChannel, 
            endChannel,
            layoutName,
            deviceIndex + 1);
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