using UnityEngine;

[RequireComponent(typeof(Light))]
public class SimpleDMXLight : DMXDevice
{
    [Header("DMX Placement")]
    [SerializeField] private bool useCustomPlacement = false;
    [SerializeField] private int customUniverse = 0;
    [SerializeField] private int customStartChannel = 0;
    [SerializeField] private bool allowAutoReassign = true;
    [SerializeField] private int devicePriority = 0;
    
    [Header("Light Configuration")]
    public Light targetLight;
    
    private Color lastColor;
    
    public override int NumChannels { get { return 4; } }
    
    public override DMXPlacement GetPreferredPlacement()
    {
        if (useCustomPlacement && customStartChannel > 0)
        {
            return DMXPlacement.Manual(customUniverse, customStartChannel, allowAutoReassign, devicePriority);
        }
        
        // Default auto-assignment
        return DMXPlacement.Auto;
    }
    
    public override void OnPlacementAssigned(int universe, int startChannel)
    {
        base.OnPlacementAssigned(universe, startChannel);
        
        // Update custom placement fields to reflect actual assignment
        if (!useCustomPlacement)
        {
            customUniverse = universe;
            customStartChannel = startChannel;
        }
    }
    
    protected override void Start()
    {
        if (targetLight == null)
            targetLight = GetComponent<Light>();
        
        if (targetLight != null)
            lastColor = targetLight.color;
        
        // Default to Output mode for sending data
        if (deviceMode == DMXDeviceMode.Input)
            deviceMode = DMXDeviceMode.Output;
        
        base.Start();
    }
    
    protected override void ProcessInputData(byte[] data)
    {
        if (data == null || data.Length < 4 || targetLight == null)
            return;
        
        var color = targetLight.color;
        
        // Apply RGB channels
        color.r = data[0] / 255f;
        color.g = data[1] / 255f;
        color.b = data[2] / 255f;
        
        // Apply white channel by adding to all RGB components
        float whiteValue = data[3] / 255f * 0.5f;
        color.r = Mathf.Clamp01(color.r + whiteValue);
        color.g = Mathf.Clamp01(color.g + whiteValue);
        color.b = Mathf.Clamp01(color.b + whiteValue);
        
        targetLight.color = color;
        lastColor = color;
    }
    
    protected override bool UpdateOutputData()
    {
        if (targetLight == null)
            return false;
        
        // Ensure outputData is initialized
        if (outputData == null)
        {
            outputData = new byte[NumChannels];
        }
        
        var currentColor = targetLight.color;
        
        // Check if color has changed
        if (Vector4.Distance(currentColor, lastColor) < 0.01f)
            return false;
        Debug.Log(currentColor);
        // Update output DMX data from light
        outputData[0] = (byte)(currentColor.r * 255);
        outputData[1] = (byte)(currentColor.g * 255);
        outputData[2] = (byte)(currentColor.b * 255);
        outputData[3] = 0; // White channel - simple implementation
        
        lastColor = currentColor;
        
        Debug.Log($"SimpleDMXLight {gameObject.name} updated output data: R={outputData[0]}, G={outputData[1]}, B={outputData[2]}, W={outputData[3]}");
        
        return true;
    }
    
    // ... rest of the methods remain the same
    
    protected override void DrawChannelValues(Vector3 basePosition)
    {
        if (deviceMode == DMXDeviceMode.Bidirectional)
        {
            // Show both input and output with proper labels
            DrawLightChannelArray(basePosition, inputData, "INPUT", Color.cyan);
            DrawLightChannelArray(basePosition + Vector3.right * 3f, outputData, "OUTPUT", Color.yellow);
        }
        else if (deviceMode == DMXDeviceMode.Input && inputData != null)
        {
            DrawLightChannelArray(basePosition, inputData, "INPUT", Color.cyan);
        }
        else if (deviceMode == DMXDeviceMode.Output && outputData != null)
        {
            DrawLightChannelArray(basePosition, outputData, "OUTPUT", Color.yellow);
        }
    }
    
    private void DrawLightChannelArray(Vector3 basePosition, byte[] data, string label, Color labelColor)
    {
        if (data == null || data.Length < 4) return;
        
        float lineHeight = 0.3f * gizmoTextSize;
        string[] channelNames = { "R", "G", "B", "W" };
        Color[] channelColors = { Color.red, Color.green, Color.blue, Color.white };
        
        // Draw label
        DrawGizmoText(basePosition, label, labelColor);
        
        // Draw channel values
        for (int i = 0; i < 4; i++)
        {
            Vector3 textPosition = basePosition + Vector3.down * (lineHeight * (i + 1));
            string channelValue = $"{channelNames[i]}{StartChannel + i}: {data[i]}";
            
            // Use channel-specific colors
            Color valueColor = channelColors[i];
            if (data[i] == 0) valueColor = Color.gray;
            
            DrawGizmoText(textPosition, channelValue, valueColor);
        }
    }
    
    protected override string GetChannelDisplayText()
    {
        string deviceName = gameObject.name;
        string modeText = deviceMode.ToString();
        return $"{deviceName} ({modeText})\nRGBW: {StartChannel}-{StartChannel + 3}";
    }
    
    // Public methods for convenient control
    public void SetRGB(float r, float g, float b)
    {
        if (inputData == null) return;
        
        inputData[0] = (byte)(r * 255);
        inputData[1] = (byte)(g * 255);
        inputData[2] = (byte)(b * 255);
        
        ProcessInputData(inputData);
    }
    
    public void SetWhite(float white)
    {
        if (inputData == null) return;
        
        inputData[3] = (byte)(white * 255);
        ProcessInputData(inputData);
    }
    
    // Helper methods to get current values
    public Color GetInputColor()
    {
        if (inputData == null || inputData.Length < 4) return Color.black;
        return new Color(inputData[0] / 255f, inputData[1] / 255f, inputData[2] / 255f, 1f);
    }
    
    public Color GetOutputColor()
    {
        if (outputData == null || outputData.Length < 4) return Color.black;
        return new Color(outputData[0] / 255f, outputData[1] / 255f, outputData[2] / 255f, 1f);
    }
}