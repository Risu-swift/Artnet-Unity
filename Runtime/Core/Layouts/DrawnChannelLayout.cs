using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DrawnChannelLayout : IChannelLayoutStrategy
{
    public string layoutName = "Drawn Layout";
    public DrawnLayoutData layoutData = new DrawnLayoutData();
    
    public void AssignChannels(DmxController.UniverseDevices universe)
    {
        if (universe.devices == null || layoutData.segments.Count == 0) return;
        
        int deviceIndex = 0;
        int currentChannel = layoutData.startChannel;
        
        foreach (var segment in layoutData.segments)
        {
            for (int i = 0; i < segment.deviceCount && deviceIndex < universe.devices.Length; i++)
            {
                var device = universe.devices[deviceIndex];
                if (device == null) continue;
                
                device.StartChannel = currentChannel;
                device.name = FormatDeviceName(device, universe, deviceIndex, segment, i);
                
                currentChannel += layoutData.channelsPerDevice;
                deviceIndex++;
            }
        }
    }
    
    private string FormatDeviceName(DMXDevice device, DmxController.UniverseDevices universe, int deviceIndex, DrawnLayoutSegment segment, int segmentIndex)
    {
        return string.Format("{0}:({1},{2:d3}-{3:d3}) {4} D{5}", 
            device.GetType().ToString(), 
            universe.universe, 
            device.StartChannel, 
            device.StartChannel + device.NumChannels - 1,
            layoutName,
            deviceIndex + 1);
    }
    
    public bool CanFitDevice(DmxController.UniverseDevices universe, IDMXDevice device)
    {
        if (universe.devices == null) return true;
        
        int totalDevices = 0;
        foreach (var segment in layoutData.segments)
        {
            totalDevices += segment.deviceCount;
        }
        
        if (universe.devices.Length >= totalDevices) return false;
        
        int nextChannel = layoutData.startChannel + (universe.devices.Length * layoutData.channelsPerDevice);
        return nextChannel + device.NumChannels <= 512;
    }
    
    public string GetLayoutName()
    {
        return layoutName;
    }
}