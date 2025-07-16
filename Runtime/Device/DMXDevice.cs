using UnityEngine;

public abstract class DMXDevice : MonoBehaviour, IDMXDevice
{
    #region Serialized Fields
    
    [Header("DMX Configuration")]
    [SerializeField] protected int universe = 0;
    [SerializeField] protected int startChannel = 1;
    [SerializeField] protected DMXDeviceMode deviceMode = DMXDeviceMode.Input;
    
    [Header("Output Configuration")]
    [SerializeField] protected bool autoUpdate = true;
    [SerializeField] protected float updateRate = 30f;
    
    [Header("Gizmo Configuration")]
    [SerializeField] protected bool showChannelGizmos = true;
    [SerializeField] protected float gizmoTextSize = 1f;
    [SerializeField] protected Vector3 gizmoOffset = new Vector3(0, 1, 0);
    [SerializeField] protected Color gizmoTextColor = Color.white;
    
    [Header("Device Naming")]
    [SerializeField] private string originalName = "";
    
    #endregion
    
    #region Private Fields
    
    protected byte[] inputData;
    protected byte[] outputData;
    protected IDMXCommunicator communicator;
    protected float lastUpdateTime;
    
    #endregion
    
    #region IDMXDevice Implementation
    
    public abstract int NumChannels { get; }
    public int StartChannel { get => startChannel; set => startChannel = value; }
    public int Universe { get => universe; set => universe = value; }
    public DMXDeviceMode DeviceMode => deviceMode;
    
    public byte[] GetInputData() => inputData;
    public byte[] GetOutputData() => outputData;
    
    public virtual void SetInputData(byte[] data)
    {
        if (data == null || data.Length != NumChannels)
        {
            Debug.LogWarning($"Invalid input DMX data for {gameObject.name}. Expected {NumChannels} channels, got {data?.Length ?? 0}.");
            return;
        }
        
        inputData = data;
        
        if (deviceMode == DMXDeviceMode.Input || deviceMode == DMXDeviceMode.Bidirectional)
        {
            ProcessInputData(inputData);
        }
    }
    
    #endregion
    
    #region Unity Lifecycle
    
    protected virtual void Start()
    {
        InitializeDataArrays();
        RegisterWithCommunicator();
    }
    
    protected virtual void Update()
    {
        HandleOutputMode();
    }
    
    protected virtual void OnDestroy()
    {
        communicator?.UnregisterDevice(this);
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeDataArrays()
    {
        if (inputData == null || inputData.Length != NumChannels)
            inputData = new byte[NumChannels];
        if (outputData == null || outputData.Length != NumChannels)
            outputData = new byte[NumChannels];
    }
    
    private void RegisterWithCommunicator()
    {
        communicator = FindFirstObjectByType<DmxController>() as IDMXCommunicator;
        if (communicator != null)
        {
            communicator.RegisterDevice(this);
        }
        else
        {
            Debug.LogWarning($"No DmxController found for device {gameObject.name}");
        }
    }
    
    private void HandleOutputMode()
    {
        if ((deviceMode == DMXDeviceMode.Output || deviceMode == DMXDeviceMode.Bidirectional) && 
            autoUpdate && 
            Time.time - lastUpdateTime > 1f / updateRate &&
            outputData != null && 
            outputData.Length >= NumChannels)
        {
            if (UpdateOutputData())
            {
                communicator?.SendData(this);
            }
            lastUpdateTime = Time.time;
        }
    }
    
    #endregion
    
    #region Abstract Methods
    
    protected abstract void ProcessInputData(byte[] data);
    protected abstract bool UpdateOutputData();
    
    #endregion
    
    #region Name Management
    
    public void StoreOriginalName()
    {
        if (string.IsNullOrEmpty(originalName))
        {
            originalName = gameObject.name;
        }
    }
    
    public string GetOriginalName() => string.IsNullOrEmpty(originalName) ? gameObject.name : originalName;
    
    public void RestoreOriginalName()
    {
        if (!string.IsNullOrEmpty(originalName))
        {
            gameObject.name = originalName;
        }
    }
    
    public string GetChannelInfo()
    {
        return startChannel > 0 ? 
            $"U{universe}:Ch{startChannel}-{startChannel + NumChannels - 1}" : 
            "Not Assigned";
    }
    
    #endregion
    
    #region Context Menu Actions
    
    [ContextMenu("Restore Original Name")]
    public void RestoreOriginalNameMenu() => RestoreOriginalName();
    
    [ContextMenu("Update Name with Channel Info")]
    public void UpdateNameWithChannelInfo()
    {
        if (communicator is DmxController controller)
        {
            controller.UpdateDeviceName(this, universe, startChannel);
        }
    }
    
    [ContextMenu("Print Channel Assignment")]
    public void PrintChannelAssignment()
    {
        Debug.Log($"Device: {GetOriginalName()}\nAssignment: {GetChannelInfo()}\nMode: {DeviceMode}");
    }
    
    #endregion
    
    #region Channel Access Methods
    
    public void SetInputChannel(int channel, byte value)
    {
        if (IsValidChannel(channel))
        {
            inputData[channel] = value;
            ProcessInputData(inputData);
        }
    }
    
    public void SetOutputChannel(int channel, byte value)
    {
        if (IsValidChannel(channel))
        {
            outputData[channel] = value;
        }
    }
    
    public byte GetInputChannel(int channel) => IsValidChannel(channel) ? inputData[channel] : (byte)0;
    public byte GetOutputChannel(int channel) => IsValidChannel(channel) ? outputData[channel] : (byte)0;
    
    private bool IsValidChannel(int channel) => channel >= 0 && channel < NumChannels;
    
    #endregion
    
    #region Legacy Compatibility
    
    public virtual void SetData(byte[] data) => SetInputData(data);
    public virtual byte[] GetDMXData() => deviceMode == DMXDeviceMode.Output ? outputData : inputData;
    public virtual void SetDMXData(byte[] data) => SetInputData(data);
    
    #endregion
    
    #region Gizmos
    
    protected virtual void OnDrawGizmos()
    {
        if (showChannelGizmos && Application.isPlaying)
        {
            DrawChannelGizmos();
        }
    }
    
    protected virtual void OnDrawGizmosSelected()
    {
        if (showChannelGizmos)
        {
            DrawChannelGizmos();
        }
    }
    
    private void DrawChannelGizmos()
    {
        if (StartChannel <= 0) return;
        
        Vector3 position = transform.position + gizmoOffset;
        
        string channelText = GetChannelDisplayText();
        DrawGizmoText(position, channelText, gizmoTextColor);
        
        DrawChannelValues(position);
    }
    
    protected virtual string GetChannelDisplayText()
    {
        return $"{GetOriginalName()} ({deviceMode})\n{GetChannelInfo()}";
    }
    
    protected virtual void DrawChannelValues(Vector3 basePosition)
    {
        switch (deviceMode)
        {
            case DMXDeviceMode.Input:
                DrawDataArray(basePosition, inputData, "IN", Color.cyan);
                break;
            case DMXDeviceMode.Output:
                DrawDataArray(basePosition, outputData, "OUT", Color.yellow);
                break;
            case DMXDeviceMode.Bidirectional:
                DrawDataArray(basePosition, inputData, "IN", Color.cyan);
                DrawDataArray(basePosition + Vector3.right * 2f, outputData, "OUT", Color.yellow);
                break;
        }
    }
    
    protected virtual void DrawDataArray(Vector3 basePosition, byte[] data, string label, Color color)
    {
        if (data == null) return;
        
        float lineHeight = 0.3f * gizmoTextSize;
        
        DrawGizmoText(basePosition + Vector3.down * lineHeight, label, color);
        
        for (int i = 0; i < data.Length && i < NumChannels; i++)
        {
            Vector3 textPosition = basePosition + Vector3.down * (lineHeight * (i + 2));
            string channelValue = $"{StartChannel + i}: {data[i]}";
            Color valueColor = GetChannelValueColor(data[i]);
            DrawGizmoText(textPosition, channelValue, valueColor);
        }
    }
    
    protected virtual Color GetChannelValueColor(byte value)
    {
        return value switch
        {
            0 => Color.gray,
            < 64 => Color.red,
            < 128 => Color.yellow,
            < 192 => Color.green,
            _ => Color.cyan
        };
    }
    
    protected virtual void DrawGizmoText(Vector3 position, string text, Color color)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(position, text, new GUIStyle()
        {
            fontSize = Mathf.RoundToInt(12 * gizmoTextSize),
            normal = new GUIStyleState() { textColor = color }
        });
        #endif
    }
    
    #endregion
}