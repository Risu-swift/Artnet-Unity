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

    [Header("Performance Settings")]
    public float sendRate = 30f;
    public bool enableBatching = true;
    public int maxUniverses = 16;
    
    [Header("Auto Discovery")]
    public bool autoDiscoverDevices = true;
    public bool discoverOnStart = true;
    
    [Header("Debug")]
    public bool showDebugLogs = false;
    
    // Network components
    private ArtNetSocket artnet;
    private IPEndPoint remote;
    private ArtNetDmxPacket dmxPacket;
    
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
        InitializeArtNet();
        dmxPacket = new ArtNetDmxPacket();
        dmxPacket.DmxData = new byte[512];
        
        LogDebug($"DMX Controller initialized - Batching: {enableBatching}, Send Rate: {sendRate}Hz");
        
        // Discover devices automatically if enabled
        if (discoverOnStart)
        {
            DiscoverDevices();
        }
    }
    
    void Update()
    {
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
        
        // Store original name if we haven't already
        if (device is DMXDevice dmxDevice)
        {
            dmxDevice.StoreOriginalName();
        }
        
        // Ask device where it wants to be placed
        var preferredPlacement = device.GetPreferredPlacement();
        var actualPlacement = ProcessPlacementRequest(device, preferredPlacement);
        
        // Assign the device to its placement
        device.OnPlacementAssigned(actualPlacement.universe, actualPlacement.startChannel);
        
        // Update GameObject name if enabled
        if (device is DMXDevice dmxDev)
        {
            UpdateDeviceName(dmxDev, actualPlacement.universe, actualPlacement.startChannel);
        }
        
        // Add to universe tracking
        if (!devicesByUniverse.ContainsKey(actualPlacement.universe))
        {
            devicesByUniverse[actualPlacement.universe] = new List<IDMXDevice>();
            universeBuffers[actualPlacement.universe] = new byte[512];
            universeDirty[actualPlacement.universe] = false;
        }
        
        if (!devicesByUniverse[actualPlacement.universe].Contains(device))
        {
            devicesByUniverse[actualPlacement.universe].Add(device);
            LogDebug($"Registered device '{device}' to Universe {actualPlacement.universe}, channels {actualPlacement.startChannel}-{actualPlacement.startChannel + device.NumChannels - 1}");
            
            OnDeviceRegistered?.Invoke(device);
        }
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
    
    private DMXPlacement ProcessPlacementRequest(IDMXDevice device, DMXPlacement preferred)
    {
        // Check if preferred placement is available
        if (preferred.startChannel > 0 && IsPlacementAvailable(preferred.universe, preferred.startChannel, device.NumChannels))
        {
            LogDebug($"Device '{device}' got preferred placement: Universe {preferred.universe}, Channel {preferred.startChannel}");
            return preferred;
        }
        
        // If preferred placement is not available, check if device allows auto-assignment
        if (preferred.autoAssign)
        {
            var suggested = SuggestPlacement(device);
            LogDebug($"Device '{device}' auto-assigned to: Universe {suggested.universe}, Channel {suggested.startChannel}");
            return suggested;
        }
        
        // If no auto-assignment allowed, try to resolve conflict based on priority
        var resolved = ResolveConflict(device, preferred);
        if (resolved.startChannel > 0)
        {
            LogDebug($"Device '{device}' placement conflict resolved: Universe {resolved.universe}, Channel {resolved.startChannel}");
            return resolved;
        }
        
        // Fallback to auto-assignment
        Debug.LogWarning($"Device '{device}' placement failed, falling back to auto-assignment");
        return SuggestPlacement(device);
    }
    
    public bool IsPlacementAvailable(int universe, int startChannel, int numChannels)
    {
        if (startChannel <= 0 || startChannel + numChannels - 1 > 512)
            return false;
        
        if (!devicesByUniverse.ContainsKey(universe))
            return true;
        
        // Check for conflicts with existing devices
        foreach (var existingDevice in devicesByUniverse[universe])
        {
            int existingStart = existingDevice.StartChannel;
            int existingEnd = existingStart + existingDevice.NumChannels - 1;
            int requestedEnd = startChannel + numChannels - 1;
            
            // Check for overlap
            if (!(requestedEnd < existingStart || startChannel > existingEnd))
            {
                return false; // Overlap detected
            }
        }
        
        return true;
    }
    
    public DMXPlacement SuggestPlacement(IDMXDevice device)
    {
        int requiredChannels = device.NumChannels;
        
        // Try to find space in existing universes first
        foreach (var kvp in devicesByUniverse.OrderBy(x => x.Key))
        {
            int universe = kvp.Key;
            var suggestion = FindAvailableChannels(universe, requiredChannels);
            if (suggestion.startChannel > 0)
            {
                return DMXPlacement.Manual(universe, suggestion.startChannel, true);
            }
        }
        
        // Create new universe if needed
        int newUniverse = GetNextAvailableUniverse();
        return DMXPlacement.Manual(newUniverse, 1, true);
    }
    
    private DMXPlacement FindAvailableChannels(int universe, int requiredChannels)
    {
        if (!devicesByUniverse.ContainsKey(universe))
        {
            return DMXPlacement.Manual(universe, 1, true);
        }
        
        // Get all occupied ranges and sort them
        var occupiedRanges = devicesByUniverse[universe]
            .Select(d => new { Start = d.StartChannel, End = d.StartChannel + d.NumChannels - 1 })
            .OrderBy(r => r.Start)
            .ToList();
        
        // Find first available gap
        int nextAvailable = 1;
        foreach (var range in occupiedRanges)
        {
            if (nextAvailable + requiredChannels - 1 < range.Start)
            {
                return DMXPlacement.Manual(universe, nextAvailable, true);
            }
            nextAvailable = range.End + 1;
        }
        
        // Check if there's space at the end
        if (nextAvailable + requiredChannels - 1 <= 512)
        {
            return DMXPlacement.Manual(universe, nextAvailable, true);
        }
        
        return DMXPlacement.Manual(universe, 0, true); // No space found
    }
    
    private DMXPlacement ResolveConflict(IDMXDevice newDevice, DMXPlacement preferred)
    {
        if (!devicesByUniverse.ContainsKey(preferred.universe))
            return preferred;
        
        // Find conflicting devices
        var conflictingDevices = devicesByUniverse[preferred.universe]
            .Where(d => {
                int existingStart = d.StartChannel;
                int existingEnd = existingStart + d.NumChannels - 1;
                int requestedEnd = preferred.startChannel + newDevice.NumChannels - 1;
                return !(requestedEnd < existingStart || preferred.startChannel > existingEnd);
            })
            .ToList();
        
        // Check if new device has higher priority than all conflicting devices
        foreach (var conflictingDevice in conflictingDevices)
        {
            if (conflictingDevice is DMXDevice dmxDevice)
            {
                var conflictingPlacement = dmxDevice.GetPreferredPlacement();
                if (conflictingPlacement.priority >= preferred.priority)
                {
                    return DMXPlacement.Manual(preferred.universe, 0, true); // Conflict not resolved
                }
            }
        }
        
        // Move conflicting devices if new device has higher priority
        foreach (var conflictingDevice in conflictingDevices)
        {
            UnregisterDevice(conflictingDevice);
            RegisterDevice(conflictingDevice); // Will trigger re-assignment
        }
        
        return preferred;
    }
    
    private int GetNextAvailableUniverse()
    {
        for (int i = 0; i < maxUniverses; i++)
        {
            if (!devicesByUniverse.ContainsKey(i))
            {
                return i;
            }
        }
        return maxUniverses - 1; // Fallback
    }
    
    #endregion
    
    #region Data Transmission
    
    public void SendData(IDMXDevice device)
    {
        if (device?.GetOutputData() == null) return;
        
        int universe = GetDeviceUniverse(device);
        
        if (!universeBuffers.ContainsKey(universe))
        {
            universeBuffers[universe] = new byte[512];
            universeDirty[universe] = false;
        }
        
        // Copy device data to universe buffer
        var outputData = device.GetOutputData();
        int startIndex = (device.StartChannel - 1) % 512; // Handle multi-universe scenarios
        
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
                    int startIndex = (device.StartChannel - 1) % 512;
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
    
    #region Device Discovery
    
    [ContextMenu("Discover Devices")]
    public void DiscoverDevices()
    {
        if (!autoDiscoverDevices)
            return;
        
        LogDebug("Discovering DMX devices in scene...");
        
        var discoveredDevices = DMXDeviceDiscovery.DiscoverDevices();
        
        foreach (var device in discoveredDevices)
        {
            RegisterDevice(device);
        }
        
        LogDebug($"Discovered and registered {discoveredDevices.Count} DMX devices");
    }
    
    #endregion
    
    #region Utility Methods
    
    private int GetDeviceUniverse(IDMXDevice device)
    {
        // Find which universe the device is currently in
        foreach (var kvp in devicesByUniverse)
        {
            if (kvp.Value.Contains(device))
            {
                return kvp.Key;
            }
        }
        
        // Fallback: calculate based on channel number
        if (device.StartChannel > 0)
        {
            return (device.StartChannel - 1) / 512;
        }
        
        return 0;
    }
    
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
                Debug.Log($"  - {device} (Ch {device.StartChannel}-{device.StartChannel + device.NumChannels - 1}, Mode: {device.DeviceMode})");
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
    
    public void SetDeviceUniverse(IDMXDevice device, int universe, int startChannel = -1)
    {
        UnregisterDevice(device);
        
        if (startChannel > 0)
        {
            device.StartChannel = startChannel;
        }
        
        RegisterDevice(device);
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