using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using UnityEngine;

using ArtNet.Sockets;
using ArtNet.Packets;

public class DmxController : MonoBehaviour, IDMXCommunicator
{
    [Header("Network Configuration")]
    public bool useBroadcast = true;
    public string remoteIP = "localhost";
    public bool useCustomPort = false;
    public int customPort = 6454;
    public bool isServer = true;
    public string deviceNameFormat = "{originalName} [U{universe}:Ch{startChannel}-{endChannel}]";
    public bool connectOnAwake = true;
    [Header("Performance Settings")]
    public float sendRate = 30f;
    public bool enableBatching = true;
    public int maxUniverses = 16;
    
    [Header("Debug")]
    public bool showDebugLogs = false;
    
    // Network components
    private ArtNetSocket artnet;
    private IPEndPoint remote;
    private ArtNetDmxPacket dmxPacket;
    private bool isInitalized = false;
    
    // Device management
    private Dictionary<int, List<IDMXDevice>> devicesByUniverse = new Dictionary<int, List<IDMXDevice>>();
    private Dictionary<int, byte[]> universeBuffers = new Dictionary<int, byte[]>();
    private Dictionary<int, bool> universeDirty = new Dictionary<int, bool>();
    private Dictionary<int, byte[]> incomingData = new Dictionary<int, byte[]>();
    
    // Timing
    private float lastSendTime;
    
    // Events
    public System.Action<int, byte[]> OnUniverseDataReceived;
    public System.Action<IDMXDevice> OnDeviceRegistered;
    public System.Action<IDMXDevice> OnDeviceUnregistered;
    
    #region Unity Lifecycle

    void Start()
    {
        if(connectOnAwake)
            Init();
    }
    void Init()
    {
        InitializeArtNet();
        dmxPacket = new ArtNetDmxPacket();
        dmxPacket.DmxData = new byte[512];
        
        LogDebug($"DMX Controller initialized - Batching: {enableBatching}, Send Rate: {sendRate}Hz");
        isInitalized = true;
    }
    
    void Update()
    {
        if(!isInitalized) return;
        ProcessIncomingData();
        
        if (enableBatching && Time.time - lastSendTime >= 1f / sendRate)
        {
            SendDirtyUniverses();
            lastSendTime = Time.time;
        }
    }
    
    void OnDestroy()
    {
        artnet?.Close();
    }
    
    #endregion
    
    #region Device Management
    
    public void RegisterDevice(IDMXDevice device)
    {
        if (device == null) return;
        
        int universe = device.Universe;
        int startChannel = device.StartChannel;
        
        // Validate universe and channel
        if (universe < 0 || universe >= maxUniverses)
        {
            Debug.LogError($"Device '{device}' has invalid universe {universe}. Must be between 0 and {maxUniverses - 1}.");
            return;
        }
        
        if (startChannel <= 0 || startChannel > 512)
        {
            Debug.LogError($"Device '{device}' has invalid start channel {startChannel}. Must be between 1 and 512.");
            return;
        }
        
        if (startChannel + device.NumChannels - 1 > 512)
        {
            Debug.LogError($"Device '{device}' channels ({startChannel} to {startChannel + device.NumChannels - 1}) exceed universe limit of 512.");
            return;
        }
        
        // Check for channel conflicts
        if (HasChannelConflict(universe, startChannel, device.NumChannels, device))
        {
            Debug.LogError($"Device '{device}' has channel conflict in universe {universe} at channel {startChannel}.");
            return;
        }
        
        // Store original name if we haven't already
        if (device is DMXDevice dmxDevice)
        {
            dmxDevice.StoreOriginalName();
        }
        
        // Update GameObject name
        if (device is DMXDevice dmxDev)
        {
            UpdateDeviceName(dmxDev, universe, startChannel);
        }
        
        // Add to universe tracking
        if (!devicesByUniverse.ContainsKey(universe))
        {
            devicesByUniverse[universe] = new List<IDMXDevice>();
            universeBuffers[universe] = new byte[512];
            universeDirty[universe] = false;
        }
        
        if (!devicesByUniverse[universe].Contains(device))
        {
            devicesByUniverse[universe].Add(device);
            LogDebug($"Registered device '{device}' to Universe {universe}, channels {startChannel}-{startChannel + device.NumChannels - 1}");
            
            OnDeviceRegistered?.Invoke(device);
        }
    }
    
    private bool HasChannelConflict(int universe, int startChannel, int numChannels, IDMXDevice excludeDevice)
    {
        if (!devicesByUniverse.ContainsKey(universe))
            return false;
        
        int endChannel = startChannel + numChannels - 1;
        
        foreach (var existingDevice in devicesByUniverse[universe])
        {
            if (existingDevice == excludeDevice) continue;
            
            int existingStart = existingDevice.StartChannel;
            int existingEnd = existingStart + existingDevice.NumChannels - 1;
            
            // Check for overlap
            if (!(endChannel < existingStart || startChannel > existingEnd))
            {
                return true; // Overlap detected
            }
        }
        
        return false;
    }
    
    public void UpdateDeviceName(DMXDevice device, int universe, int startChannel)
    {
        if (device == null) return;
        
        string originalName = device.GetOriginalName();
        int endChannel = startChannel + device.NumChannels - 1;
        
        string newName = deviceNameFormat
            .Replace("{originalName}", originalName)
            .Replace("{universe}", universe.ToString())
            .Replace("{startChannel}", startChannel.ToString())
            .Replace("{endChannel}", endChannel.ToString())
            .Replace("{numChannels}", device.NumChannels.ToString())
            .Replace("{deviceMode}", device.DeviceMode.ToString());
        
        device.gameObject.name = newName;
        
        LogDebug($"Updated device name: '{originalName}' -> '{newName}'");
    }
    
    public void UnregisterDevice(IDMXDevice device)
    {
        if (device == null) return;
        
        // Remove from all universes
        foreach (var kvp in devicesByUniverse.ToArray())
        {
            if (kvp.Value.Remove(device))
            {
                LogDebug($"Unregistered device '{device}' from Universe {kvp.Key}");
                
                // Clean up empty universes
                if (kvp.Value.Count == 0)
                {
                    devicesByUniverse.Remove(kvp.Key);
                    universeBuffers.Remove(kvp.Key);
                    universeDirty.Remove(kvp.Key);
                }
            }
        }
        
        OnDeviceUnregistered?.Invoke(device);
    }
    
    #endregion
    
    #region Data Transmission
    
    public void SendData(IDMXDevice device)
    {
        if (device?.GetOutputData() == null) return;
        
        int universe = device.Universe;
        
        if (!universeBuffers.ContainsKey(universe))
        {
            universeBuffers[universe] = new byte[512];
            universeDirty[universe] = false;
        }
        
        // Copy device data to universe buffer
        var outputData = device.GetOutputData();
        int startIndex = device.StartChannel - 1; // Convert to 0-based index
        
        for (int i = 0; i < outputData.Length && (startIndex + i) < 512; i++)
        {
            universeBuffers[universe][startIndex + i] = outputData[i];
        }
        
        universeDirty[universe] = true;
        
        // Send immediately if batching is disabled
        if (!enableBatching)
        {
            SendUniverse(universe);
        }
    }
    
    private void SendDirtyUniverses()
    {
        foreach (var kvp in universeDirty.ToArray())
        {
            if (kvp.Value)
            {
                SendUniverse(kvp.Key);
                universeDirty[kvp.Key] = false;
            }
        }
    }
    
    private void SendUniverse(int universe)
    {
        if (!universeBuffers.ContainsKey(universe)) return;
        
        dmxPacket.Universe = (short)universe;
        System.Buffer.BlockCopy(universeBuffers[universe], 0, dmxPacket.DmxData, 0, 512);
        
        try
        {
            if (useBroadcast && isServer)
            {
                artnet.Send(dmxPacket);
            }
            else
            {
                artnet.Send(dmxPacket, remote);
            }
            
            LogDebug($"Sent Art-Net data for universe {universe}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to send Art-Net data for universe {universe}: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Data Reception
    
    private void InitializeArtNet()
    {
        artnet = new ArtNetSocket();
        
        if (isServer)
        {
            artnet.Open(FindFromHostName("localhost"), null);
        }
        
        artnet.NewPacket += OnArtNetPacketReceived;
        
        if (!useBroadcast || !isServer)
        {
            int port = useCustomPort ? customPort : ArtNetSocket.Port;
            remote = new IPEndPoint(FindFromHostName(remoteIP), port);
        }
    }
    
    private void OnArtNetPacketReceived(object sender, NewPacketEventArgs<ArtNetPacket> e)
    {
        if (e.Packet is ArtNetDmxPacket dmxPacket)
        {
            int universe = dmxPacket.Universe;
            incomingData[universe] = dmxPacket.DmxData;
            OnUniverseDataReceived?.Invoke(universe, dmxPacket.DmxData);
            
            LogDebug($"Received Art-Net data for universe {universe}");
        }
    }
    
    private void ProcessIncomingData()
    {
        foreach (var kvp in incomingData.ToArray())
        {
            int universe = kvp.Key;
            byte[] data = kvp.Value;
            
            if (data == null || !devicesByUniverse.ContainsKey(universe)) continue;
            
            // Send data to all input devices in this universe
            foreach (var device in devicesByUniverse[universe])
            {
                if (device.DeviceMode == DMXDeviceMode.Input || device.DeviceMode == DMXDeviceMode.Bidirectional)
                {
                    int startIndex = device.StartChannel - 1; // Convert to 0-based index
                    if (startIndex >= 0 && startIndex < data.Length)
                    {
                        int length = Mathf.Min(device.NumChannels, data.Length - startIndex);
                        byte[] deviceData = new byte[length];
                        System.Array.Copy(data, startIndex, deviceData, 0, length);
                        device.SetInputData(deviceData);
                    }
                }
            }
            
            incomingData[universe] = null;
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    private void LogDebug(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[DMXController] {message}");
        }
    }
    
    [ContextMenu("Print Device Info")]
    public void PrintDeviceInfo()
    {
        Debug.Log("=== DMX Controller Device Info ===");
        Debug.Log($"Total Devices: {GetDeviceCount()}");
        Debug.Log($"Active Universes: {GetUniverseCount()}");
        
        foreach (var kvp in devicesByUniverse)
        {
            int usedChannels = GetUsedChannelsInUniverse(kvp.Key);
            Debug.Log($"Universe {kvp.Key}: {kvp.Value.Count} devices, {usedChannels}/512 channels used");
            
            foreach (var device in kvp.Value)
            {
                Debug.Log($"  - {device} (U{device.Universe}:Ch{device.StartChannel}-{device.StartChannel + device.NumChannels - 1}, Mode: {device.DeviceMode})");
            }
        }
    }
    
    [ContextMenu("Force Send All")]
    public void ForceSendAll()
    {
        foreach (var universe in universeBuffers.Keys)
        {
            SendUniverse(universe);
        }
    }
    
    [ContextMenu("Clear All Devices")]
    public void ClearAllDevices()
    {
        var allDevices = devicesByUniverse.Values.SelectMany(list => list).ToArray();
        foreach (var device in allDevices)
        {
            UnregisterDevice(device);
        }
        
        Debug.Log("Cleared all registered devices");
    }
    
    private int GetUsedChannelsInUniverse(int universe)
    {
        if (!devicesByUniverse.ContainsKey(universe)) return 0;
        
        int maxChannel = 0;
        foreach (var device in devicesByUniverse[universe])
        {
            maxChannel = Mathf.Max(maxChannel, device.StartChannel + device.NumChannels - 1);
        }
        return maxChannel;
    }
    
    public int GetDeviceCount() => devicesByUniverse.Values.Sum(list => list.Count);
    public int GetUniverseCount() => devicesByUniverse.Count;
    
    private static IPAddress FindFromHostName(string hostname)
    {
        var address = IPAddress.None;
        try
        {
            if (IPAddress.TryParse(hostname, out address))
                return address;

            var addresses = Dns.GetHostAddresses(hostname);
            for (var i = 0; i < addresses.Length; i++)
            {
                if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    address = addresses[i];
                    break;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to find IP for hostname '{hostname}': {e.Message}");
        }
        return address;
    }
    
    #endregion
    
    #region Gizmos
    
    [Header("Gizmo Configuration")]
    [SerializeField] private bool showUniverseGizmos = true;
    [SerializeField] private float gizmoTextSize = 1f;
    [SerializeField] private Vector3 gizmoOffset = new Vector3(0, 2, 0);
    [SerializeField] private Color universeTextColor = Color.cyan;
    
    private void OnDrawGizmos()
    {
        if (showUniverseGizmos && devicesByUniverse != null)
        {
            DrawUniverseGizmos();
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        if (showUniverseGizmos && devicesByUniverse != null)
        {
            DrawUniverseGizmos();
        }
    }
    
    private void DrawUniverseGizmos()
    {
        Vector3 controllerPosition = transform.position + gizmoOffset;
        
        string controllerInfo = $"DMX Controller\nDevices: {GetDeviceCount()}\nUniverses: {GetUniverseCount()}";
        DrawGizmoText(controllerPosition, controllerInfo, universeTextColor);
        
        int i = 0;
        foreach (var kvp in devicesByUniverse.OrderBy(x => x.Key))
        {
            int universe = kvp.Key;
            var devices = kvp.Value;
            
            Vector3 universePosition = controllerPosition + Vector3.down * (0.5f * gizmoTextSize * (i + 2));
            string universeInfo = GetUniverseInfo(universe, devices);
            DrawGizmoText(universePosition, universeInfo, GetUniverseColor(universe));
            
            i++;
        }
    }
    
    private string GetUniverseInfo(int universe, List<IDMXDevice> devices)
    {
        int deviceCount = devices.Count;
        int channelCount = GetUsedChannelsInUniverse(universe);
        
        return $"Universe {universe}: {deviceCount} devices, {channelCount}/512 channels";
    }
    
    private Color GetUniverseColor(int universe)
    {
        int channelCount = GetUsedChannelsInUniverse(universe);
        
        if (channelCount == 0) return Color.gray;
        if (channelCount > 512) return Color.red;
        if (channelCount > 400) return Color.yellow;
        return Color.green;
    }
    
    private void DrawGizmoText(Vector3 position, string text, Color color)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(position, text, new GUIStyle()
        {
            fontSize = Mathf.RoundToInt(12 * gizmoTextSize),
            normal = new GUIStyleState() { textColor = color },
            alignment = TextAnchor.MiddleCenter
        });
        #endif
    }
    
    #endregion
}