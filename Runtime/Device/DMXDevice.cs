using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public abstract class DMXDevice : MonoBehaviour, IDMXDevice, IDMXChannelProcessor
{
    [Header("DMX Configuration")]
    [SerializeField] protected byte[] dmxData;
    [SerializeField] protected int startChannel;
    [SerializeField] protected DMXDeviceMode deviceMode = DMXDeviceMode.Input;
    
    [Header("Output Configuration")]
    [SerializeField] protected bool autoUpdate = true;
    [SerializeField] protected float updateRate = 30f;
    
    protected IDMXCommunicator communicator;
    protected DMXCommandInvoker commandInvoker;
    protected IDMXDataProvider dataProvider;
    protected IDMXDataConsumer dataConsumer;
    protected float lastUpdateTime;
    
    // Channel mapping for command pattern
    protected Dictionary<ChannelFunction, int> channelMap = new Dictionary<ChannelFunction, int>();
    
    // IDMXDevice interface implementations
    public abstract int NumChannels { get; }
    public int StartChannel { get { return startChannel; } set { startChannel = value; } }
    public DMXDeviceMode DeviceMode { get { return deviceMode; } }
    
    public byte[] GetDMXData()
    {
        return dmxData;
    }
    
    public virtual void SetDMXData(byte[] data)
    {
        // Only reject external DMX data in Output mode, not internal updates
        if (deviceMode == DMXDeviceMode.Output)
        {
            // In Output mode, we only accept data updates from internal sources (data providers)
            // This is called by ReadFromGameObjectCommand, so we need to allow it
            UpdateInternalDMXData(data);
            return;
        }
            
        if (data == null)
        {
            Debug.LogWarning("Received null DMX data");
            return;
        }
        
        if (data.Length != NumChannels)
        {
            Debug.LogWarning($"Data length mismatch. Expected {NumChannels}, got {data.Length}");
            // Resize data to match expected length
            var resizedData = new byte[NumChannels];
            System.Array.Copy(data, resizedData, Mathf.Min(data.Length, NumChannels));
            data = resizedData;
        }
        
        dmxData = data;
        
        if (deviceMode == DMXDeviceMode.Input || deviceMode == DMXDeviceMode.Bidirectional)
        {
            dataConsumer?.ApplyData(dmxData);
        }
    }
    
    // Internal method to update DMX data (used by data providers)
    protected virtual void UpdateInternalDMXData(byte[] data)
    {
        if (data == null)
        {
            Debug.LogWarning("Received null DMX data");
            return;
        }
        
        if (data.Length != NumChannels)
        {
            Debug.LogWarning($"Data length mismatch. Expected {NumChannels}, got {data.Length}");
            // Resize data to match expected length
            var resizedData = new byte[NumChannels];
            System.Array.Copy(data, resizedData, Mathf.Min(data.Length, NumChannels));
            data = resizedData;
        }
        
        dmxData = data;
        
        // Debug output to verify data is being updated
        Debug.Log($"DMX data updated for {gameObject.name}: [{string.Join(", ", dmxData)}]");
    }
    
    // Legacy method for backward compatibility
    public virtual void SetData(byte[] data)
    {
        SetDMXData(data);
    }
    
    // IDMXChannelProcessor interface implementations
    public virtual void ProcessChannel(ChannelFunction function, byte value, byte fineValue = 0)
    {
        if (channelMap.ContainsKey(function))
        {
            var command = new SetChannelCommand(this, channelMap[function], value);
            commandInvoker.ExecuteCommand(command);
            
            // Handle fine channel if provided
            if (fineValue > 0)
            {
                var fineFunction = GetFineChannelFunction(function);
                if (channelMap.ContainsKey(fineFunction))
                {
                    var fineCommand = new SetChannelCommand(this, channelMap[fineFunction], fineValue);
                    commandInvoker.ExecuteCommand(fineCommand);
                }
            }
        }
    }
    
    public virtual byte ReadChannel(ChannelFunction function)
    {
        if (channelMap.ContainsKey(function))
        {
            int channelIndex = channelMap[function];
            if (dmxData != null && channelIndex < dmxData.Length)
                return dmxData[channelIndex];
        }
        return 0;
    }
    
    // Unity lifecycle methods
    protected virtual void Start()
    {
        communicator = FindFirstObjectByType<DmxController>() as IDMXCommunicator;
        commandInvoker = new DMXCommandInvoker();
        
        if (dmxData == null)
            dmxData = new byte[NumChannels];
            
        InitializeChannelMap();
        InitializeDataProviderAndConsumer();
        
        if (communicator != null)
            communicator.RegisterDevice(this);
    }
    
    protected virtual void Update()
    {
        if (deviceMode == DMXDeviceMode.Output || deviceMode == DMXDeviceMode.Bidirectional)
        {
            if (autoUpdate && Time.time - lastUpdateTime > 1f / updateRate)
            {
                if (dataProvider != null)
                {
                    var readCommand = new ReadFromGameObjectCommand(this, dataProvider);
                    commandInvoker.ExecuteCommand(readCommand);
                }
                
                // Send data to controller (will be batched)
                if (communicator != null)
                    communicator.SendData(this);
                    
                lastUpdateTime = Time.time;
            }
        }
    }
    
    protected virtual void OnDestroy()
    {
        if (communicator != null)
            communicator.UnregisterDevice(this);
    }
    
    // Abstract methods that derived classes must implement
    protected abstract void InitializeChannelMap();
    protected abstract void InitializeDataProviderAndConsumer();
    
    // Helper methods
    protected virtual ChannelFunction GetFineChannelFunction(ChannelFunction baseFunction)
    {
        switch (baseFunction)
        {
            case ChannelFunction.Color_R: return ChannelFunction.Color_RFine;
            case ChannelFunction.Color_G: return ChannelFunction.Color_GFine;
            case ChannelFunction.Color_B: return ChannelFunction.Color_BFine;
            case ChannelFunction.Color_W: return ChannelFunction.Color_WFine;
            case ChannelFunction.Pan: return ChannelFunction.PanFine;
            case ChannelFunction.Tilt: return ChannelFunction.TiltFine;
            case ChannelFunction.Intensity: return ChannelFunction.IntensityFine;
            default: return ChannelFunction.Unknown;
        }
    }
    
    // Convenience methods for common operations
    public void SetColor(Color color)
    {
        var command = new SetColorCommand(this, color, 
            channelMap.ContainsKey(ChannelFunction.Color_R) ? channelMap[ChannelFunction.Color_R] : 0,
            channelMap.ContainsKey(ChannelFunction.Color_G) ? channelMap[ChannelFunction.Color_G] : 1,
            channelMap.ContainsKey(ChannelFunction.Color_B) ? channelMap[ChannelFunction.Color_B] : 2);
        commandInvoker.ExecuteCommand(command);
    }
    
    public void UndoLastCommand()
    {
        commandInvoker?.UndoLastCommand();
    }
    
    public void SetIntensity(float intensity)
    {
        if (channelMap.ContainsKey(ChannelFunction.Intensity))
        {
            var command = new SetChannelCommand(this, channelMap[ChannelFunction.Intensity], (byte)(intensity * 255));
            commandInvoker.ExecuteCommand(command);
        }
    }
    
    public void SetDimmer(float dimmer)
    {
        if (channelMap.ContainsKey(ChannelFunction.Dimmer))
        {
            var command = new SetChannelCommand(this, channelMap[ChannelFunction.Dimmer], (byte)(dimmer * 255));
            commandInvoker.ExecuteCommand(command);
        }
    }
}