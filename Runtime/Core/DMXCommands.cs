using UnityEngine;
using System.Collections.Generic;
using System;

// Base command class
public abstract class DMXCommand : IDMXCommand
{
    protected IDMXDevice device;
    private byte[] previousData;
    
    public DMXCommand(IDMXDevice device)
    {
        this.device = device;
    }
    
    public abstract void Execute();
    
    public virtual void Undo()
    {
        if (previousData != null)
            device.SetDMXData(previousData);
    }
    
    protected void BackupCurrentData()
    {
        var currentData = device.GetDMXData();
        if (currentData != null)
        {
            previousData = new byte[currentData.Length];
            Array.Copy(currentData, previousData, currentData.Length);
        }
    }
}

// Specific command implementations
public class SetChannelCommand : DMXCommand
{
    private int channelIndex;
    private byte value;
    
    public SetChannelCommand(IDMXDevice device, int channelIndex, byte value) : base(device)
    {
        this.channelIndex = channelIndex;
        this.value = value;
    }
    
    public override void Execute()
    {
        BackupCurrentData();
        
        var currentData = device.GetDMXData();
        if (currentData != null && channelIndex < currentData.Length)
        {
            currentData[channelIndex] = value;
            device.SetDMXData(currentData);
        }
    }
}

public class SetColorCommand : DMXCommand
{
    private Color color;
    private int redChannel, greenChannel, blueChannel;
    
    public SetColorCommand(IDMXDevice device, Color color, int redChannel = 0, int greenChannel = 1, int blueChannel = 2) : base(device)
    {
        this.color = color;
        this.redChannel = redChannel;
        this.greenChannel = greenChannel;
        this.blueChannel = blueChannel;
    }
    
    public override void Execute()
    {
        BackupCurrentData();
        
        var currentData = device.GetDMXData();
        if (currentData != null)
        {
            if (redChannel < currentData.Length) currentData[redChannel] = (byte)(color.r * 255);
            if (greenChannel < currentData.Length) currentData[greenChannel] = (byte)(color.g * 255);
            if (blueChannel < currentData.Length) currentData[blueChannel] = (byte)(color.b * 255);
            
            device.SetDMXData(currentData);
        }
    }
}

public class ReadFromGameObjectCommand : DMXCommand
{
    private IDMXDataProvider dataProvider;
    
    public ReadFromGameObjectCommand(IDMXDevice device, IDMXDataProvider dataProvider) : base(device)
    {
        this.dataProvider = dataProvider;
    }
    
    public override void Execute()
    {
        BackupCurrentData();
        
        var newData = dataProvider.ReadData();
        if (newData != null)
            device.SetDMXData(newData);
    }
}