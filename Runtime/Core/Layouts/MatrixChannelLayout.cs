// Matrix layout implementation that calculates your pattern automatically

using UnityEngine;

[System.Serializable]
public class MatrixChannelLayout : IChannelLayoutStrategy
{
    [Header("Matrix Configuration")]
    public string layoutName = "Matrix Layout";
    public int channelsPerDevice = 4;
    public int devicesPerRow = 8;
    public int baseChannel = 1;
    public int rowSpacing = 16;
    public int normalColumnSpacing = 4;
    public int gapAfterColumn = 3; // 0-indexed (gap after 4th device)
    public int gapSize = 32; // Additional spacing for gap
    
    public void AssignChannels(DmxController.UniverseDevices universe)
    {
        if (universe.devices == null) return;
        
        for (int i = 0; i < universe.devices.Length; i++)
        {
            var device = universe.devices[i];
            if (device == null) continue;
            
            device.StartChannel = CalculateChannelForDevice(i);
            device.name = FormatDeviceName(device, universe, i);
        }
    }
    
    private int CalculateChannelForDevice(int deviceIndex)
    {
        int row = deviceIndex / devicesPerRow;
        int col = deviceIndex % devicesPerRow;
        
        // Calculate base channel for this row
        int rowBaseChannel = baseChannel + (row * rowSpacing);
        
        // Calculate column offset
        int columnOffset = col * normalColumnSpacing;
        
        // Add gap if this column is after the gap position
        if (col > gapAfterColumn)
        {
            columnOffset += gapSize;
        }
        
        return rowBaseChannel + columnOffset;
    }
    
    private string FormatDeviceName(DMXDevice device, DmxController.UniverseDevices universe, int deviceIndex)
    {
        int row = deviceIndex / devicesPerRow;
        int col = deviceIndex % devicesPerRow;
        
        return string.Format("{0}:({1},{2:d3}-{3:d3}) {4} R{5}C{6}", 
            device.GetType().ToString(), 
            universe.universe, 
            device.StartChannel, 
            device.StartChannel + device.NumChannels - 1,
            layoutName,
            row + 1, 
            col + 1);
    }
    
    public bool CanFitDevice(DmxController.UniverseDevices universe, IDMXDevice device)
    {
        if (universe.devices == null) return true;
        
        // Calculate what the channel would be for the next device
        int nextDeviceIndex = universe.devices.Length;
        int nextChannel = CalculateChannelForDevice(nextDeviceIndex);
        
        return nextChannel + device.NumChannels <= 512;
    }
    
    public string GetLayoutName()
    {
        return layoutName;
    }
}
