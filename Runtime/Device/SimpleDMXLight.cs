using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Light))]
public class SimpleDMXLight : DMXDevice
{
    new public Light light;

    public override int NumChannels { get { return 4; } }

    protected override void InitializeChannelMap()
    {
        // Define the channel mapping for simple RGB + White light
        channelMap[ChannelFunction.Color_R] = 0;
        channelMap[ChannelFunction.Color_G] = 1;
        channelMap[ChannelFunction.Color_B] = 2;
        channelMap[ChannelFunction.Color_W] = 3;
    }

    protected override void InitializeDataProviderAndConsumer()
    {
        // Create data consumer for applying DMX data to the light
        dataConsumer = new SimpleLightDataConsumer(this);
        
        // Create data provider for reading light state (for output mode)
        dataProvider = new SimpleLightDataProvider(this);
    }

    protected override void DrawChannelValues(Vector3 basePosition)
    {
        if (dmxData == null || dmxData.Length < 4) return;
        
        float lineHeight = 0.3f * gizmoTextSize;
        string[] channelNames = { "R", "G", "B", "W" };
        Color[] channelColors = { Color.red, Color.green, Color.blue, Color.white };
        
        for (int i = 0; i < 4; i++)
        {
            Vector3 textPosition = basePosition + Vector3.down * (lineHeight * (i + 1));
            string channelValue = $"{channelNames[i]}{StartChannel + i}: {dmxData[i]}";
            
            // Use channel-specific colors
            Color valueColor = channelColors[i];
            if (dmxData[i] == 0) valueColor = Color.gray;
            
            DrawGizmoText(textPosition, channelValue, valueColor);
        }
    }

    protected override string GetChannelDisplayText()
    {
        string deviceName = gameObject.name;
        return $"{deviceName}\nRGBW: {StartChannel}-{StartChannel + 3}";
    }
    public override void SetDMXData(byte[] data)
    {
        base.SetDMXData(data);
        
        // Process the DMX data and update the light (only for Input/Bidirectional modes)
        if (data != null && data.Length >= NumChannels && 
            (deviceMode == DMXDeviceMode.Input || deviceMode == DMXDeviceMode.Bidirectional))
        {
            ProcessDMXData(data);
        }
    }

    private void ProcessDMXData(byte[] data)
    {
        if (light == null) return;

        var color = light.color;

        // Apply RGB channels
        color.r = data[0] / 255f;
        color.g = data[1] / 255f;
        color.b = data[2] / 255f;
        
        // Apply white channel by adding to all RGB components
        float whiteValue = data[3] / 255f * 0.5f;
        color.r += whiteValue;
        color.g += whiteValue;
        color.b += whiteValue;

        // Clamp values to ensure they don't exceed 1.0
        color.r = Mathf.Clamp01(color.r);
        color.g = Mathf.Clamp01(color.g);
        color.b = Mathf.Clamp01(color.b);

        light.color = color;
    }

    protected override void Start()
    {
        light = GetComponent<Light>();
        base.Start(); // Call base Start to initialize the DMX system
    }

    // Public methods for convenient control
    public void SetRGB(float r, float g, float b)
    {
        SetColor(new Color(r, g, b, 1f)); // Uses inherited DMXDevice.SetColor
    }

    public void SetWhite(float white)
    {
        ProcessChannel(ChannelFunction.Color_W, (byte)(white * 255));
    }

    // This method is called by the data provider to update DMX data from light state
    public void UpdateDMXDataFromLight()
    {
        // Ensure dmxData is initialized
        if (dmxData == null || dmxData.Length != NumChannels)
        {
            dmxData = new byte[NumChannels];
        }

        if (light != null)
        {
            var color = light.color;
            dmxData[0] = (byte)(color.r * 255);
            dmxData[1] = (byte)(color.g * 255);
            dmxData[2] = (byte)(color.b * 255);
            // White channel calculation is more complex in reverse, so we'll keep it simple
            dmxData[3] = 0; // Could implement white extraction logic here
        }
    }
}

// Data consumer for applying DMX data to the light
public class SimpleLightDataConsumer : IDMXDataConsumer
{
    private SimpleDMXLight light;
    
    public SimpleLightDataConsumer(SimpleDMXLight light)
    {
        this.light = light;
    }
    
    public void ApplyData(byte[] data)
    {
        // The SimpleDMXLight already handles data processing in SetDMXData
        // This could be used for additional processing if needed
    }
}

// Data provider for reading light state
public class SimpleLightDataProvider : IDMXDataProvider
{
    private SimpleDMXLight lightDevice;
    
    public SimpleLightDataProvider(SimpleDMXLight lightDevice)
    {
        this.lightDevice = lightDevice;
    }
    
    public byte[] ReadData()
    {
        // Update the device's internal DMX data from the light
        lightDevice.UpdateDMXDataFromLight();
        
        // Return the updated DMX data
        return lightDevice.GetDMXData();
    }
}