using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using UnityEngine;

using ArtNet.Sockets;
using ArtNet.Packets;

public class DmxController : MonoBehaviour, IDMXCommunicator
{
    [Header("send dmx")]
    public bool useBroadcast;
    public string remoteIP = "localhost";
    
    [Header("Port Configuration")]
    public bool useCustomPort = false;
    public int customPort = 6454; // Default Art-Net port
    
    [Header("Batched Sending")]
    public float batchSendRate = 30f; // How often to send batched data
    public bool enableBatchedSending = true;
    
    [Header("Auto Channel Assignment")]
    public bool autoAssignChannels = true; // Enable automatic channel assignment
    
    IPEndPoint remote;

    [Header("dmx devices")]
    public UniverseDevices[] universes;
    public bool isServer;

    ArtNetSocket artnet;
    [Header("send/recieved DMX data for debug")]
    [SerializeField] ArtNetDmxPacket latestReceivedDMX;
    [SerializeField] ArtNetDmxPacket dmxToSend;
    byte[] _dmxData;

    Dictionary<int, byte[]> dmxDataMap;
    private List<IDMXDevice> registeredDevices = new List<IDMXDevice>();
    
    // Universe output buffers and batching
    private Dictionary<int, byte[]> universeOutputBuffers = new Dictionary<int, byte[]>();
    private Dictionary<int, bool> universeDirtyFlags = new Dictionary<int, bool>();
    private float lastBatchSendTime;

    public bool newPacket;
    // IDMXCommunicator interface implementations
    public void SendData(IDMXDevice device)
    {
        if (!enableBatchedSending)
        {
            // Fall back to immediate sending if batching is disabled
            SendDataImmediate(device);
            return;
        }
        
        if (device.GetDMXData() == null || device.GetDMXData().Length == 0)
            return;
            
        // Find which universe this device belongs to
        var universeDevices = universes.FirstOrDefault(u => u.devices.Contains(device as DMXDevice));
        if (universeDevices == null)
        {
            Debug.LogWarning($"Device {device} not found in any universe!");
            return;
        }
        
        int universeId = universeDevices.universe;
        
        // Get or create the output buffer for this universe
        if (!universeOutputBuffers.ContainsKey(universeId))
        {
            universeOutputBuffers[universeId] = new byte[512];
        }
        
        byte[] universeBuffer = universeOutputBuffers[universeId];
        
        // Copy device data to the correct channel positions in the universe buffer
        var deviceData = device.GetDMXData();
        for (int i = 0; i < deviceData.Length && (device.StartChannel + i) < 512; i++)
        {
            universeBuffer[device.StartChannel + i] = deviceData[i];
        }
        
        // Mark this universe as dirty (needs to be sent)
        universeDirtyFlags[universeId] = true;
        
        // Debug: Show what we're putting in the buffer
        Debug.Log($"Device {device} updated universe {universeId} channels {device.StartChannel}-{device.StartChannel + deviceData.Length - 1}: [{string.Join(", ", deviceData)}]");
    }
    
    // Immediate sending (fallback for when batching is disabled)
    private void SendDataImmediate(IDMXDevice device)
    {
        if (device.GetDMXData() == null || device.GetDMXData().Length == 0)
            return;
            
        // Find which universe this device belongs to
        var universeDevices = universes.FirstOrDefault(u => u.devices.Contains(device as DMXDevice));
        if (universeDevices == null)
        {
            Debug.LogWarning($"Device {device} not found in any universe!");
            return;
        }
        
        int universeId = universeDevices.universe;
        
        // Get or create the output buffer for this universe
        if (!universeOutputBuffers.ContainsKey(universeId))
        {
            universeOutputBuffers[universeId] = new byte[512];
        }
        
        byte[] universeBuffer = universeOutputBuffers[universeId];
        
        // Copy device data to the correct channel positions in the universe buffer
        var deviceData = device.GetDMXData();
        for (int i = 0; i < deviceData.Length && (device.StartChannel + i) < 512; i++)
        {
            universeBuffer[device.StartChannel + i] = deviceData[i];
        }
        
        // Send immediately
        Send((short)universeId, universeBuffer);
    }
    
    // Method to send all dirty universes (called in Update)
    private void SendBatchedData()
    {
        if (Time.time - lastBatchSendTime < 1f / batchSendRate)
            return;
            
        // Create a copy of the keys to avoid modification during enumeration
        var universesToSend = new List<int>();
        
        // First, collect all dirty universes
        foreach (var kvp in universeDirtyFlags)
        {
            if (kvp.Value && universeOutputBuffers.ContainsKey(kvp.Key))
            {
                universesToSend.Add(kvp.Key);
            }
        }
        
        // Then send them and clear dirty flags
        foreach (int universeId in universesToSend)
        {
            Send((short)universeId, universeOutputBuffers[universeId]);
            universeDirtyFlags[universeId] = false; // Clear dirty flag
            
            // Debug: Show what we're sending
            var buffer = universeOutputBuffers[universeId];
            var nonZeroChannels = buffer.Select((value, index) => new { value, index })
                                       .Where(x => x.value > 0)
                                       .ToArray();
            
            if (nonZeroChannels.Length > 0)
            {
                Debug.Log($"Sent universe {universeId} - Non-zero channels: {string.Join(", ", nonZeroChannels.Select(x => $"Ch{x.index}={x.value}"))}");
            }
        }
        
        lastBatchSendTime = Time.time;
    }
    
    // Force send all universes immediately (useful for testing)
    [ContextMenu("Force Send All Universes")]
    public void ForceSendAllUniverses()
    {
        // Use ToArray() to avoid modification during enumeration
        foreach (var kvp in universeOutputBuffers.ToArray())
        {
            int universeId = kvp.Key;
            byte[] buffer = kvp.Value;
            Send((short)universeId, buffer);
        }
    }
    
    public void RegisterDevice(IDMXDevice device)
    {
        if (!registeredDevices.Contains(device))
        {
            registeredDevices.Add(device);
            
            // Auto-assign channels if enabled
            if (autoAssignChannels)
            {
                AssignDeviceToUniverse(device);
            }
        }
    }
    
    public void UnregisterDevice(IDMXDevice device)
    {
        registeredDevices.Remove(device);
    }
    
    [Header("Channel Layout")]
    public IChannelLayoutStrategy layoutStrategy;
    
    // Serialized fields for inspector configuration
    [SerializeField] private LayoutType currentLayoutType = LayoutType.Sequential;
    [SerializeField] private MatrixChannelLayout matrixLayout = new MatrixChannelLayout();
    [SerializeField] private SequentialChannelLayout sequentialLayout = new SequentialChannelLayout();
    
    public enum LayoutType
    {
        Sequential,
        Matrix,
        Drawn
    }
    
    void Start()
    {
        // Initialize layout strategy based on selection
        UpdateLayoutStrategy();
        
        artnet = new ArtNetSocket();
        if (isServer)
            artnet.Open(FindFromHostName("localhost"), null);
        
        dmxToSend.DmxData = new byte[512];

        artnet.NewPacket += (object sender, NewPacketEventArgs<ArtNetPacket> e) =>
        {
            newPacket = true;
            if (e.Packet.OpCode == ArtNet.Enums.ArtNetOpCodes.Dmx)
            {
                var packet = latestReceivedDMX = e.Packet as ArtNetDmxPacket;

                if (packet.DmxData != _dmxData)
                    _dmxData = packet.DmxData;

                var universe = packet.Universe;
                if (dmxDataMap.ContainsKey(universe))
                    dmxDataMap[universe] = packet.DmxData;
                else
                    dmxDataMap.Add(universe, packet.DmxData);
            }
        };

        if (!useBroadcast || !isServer)
        {
            int portToUse = useCustomPort ? customPort : ArtNetSocket.Port;
            remote = new IPEndPoint(FindFromHostName(remoteIP), portToUse);
        }

        dmxDataMap = new Dictionary<int, byte[]>();
        universeOutputBuffers = new Dictionary<int, byte[]>();
        universeDirtyFlags = new Dictionary<int, bool>();
        
        // Initialize all universes on start
        if (universes != null)
        {
            foreach (var universe in universes)
            {
                universe.Initialize();
            }
        }
    }
    
    [SerializeField] private DrawnChannelLayout drawnLayout = new DrawnChannelLayout();
    
    private void UpdateLayoutStrategy()
    {
        switch (currentLayoutType)
        {
            case LayoutType.Sequential:
                layoutStrategy = sequentialLayout;
                break;
            case LayoutType.Matrix:
                layoutStrategy = matrixLayout;
                break;
            case LayoutType.Drawn:
                layoutStrategy = drawnLayout;
                break;
        }
    }
    
    // Method to apply drawn layout from the editor window
    public void ApplyDrawnLayout(DrawnLayoutData layoutData)
    {
        drawnLayout.layoutData = layoutData;
        currentLayoutType = LayoutType.Drawn;
        UpdateLayoutStrategy();
        ReassignAllChannels();
    }
    
    // Called when inspector values change
    private void OnValidate()
    {
        UpdateLayoutStrategy();
        
        if (universes != null)
        {
            foreach (var u in universes)
            {
                if (layoutStrategy != null)
                {
                    layoutStrategy.AssignChannels(u);
                }
                else
                {
                    u.Initialize(); // Fallback
                }
            }
        }
    }
    
    // Modified auto-assignment method - now uses only the interface
    private void AssignDeviceToUniverse(IDMXDevice device)
    {
        var dmxDevice = device as DMXDevice;
        if (dmxDevice == null) return;
        
        // Find the first universe that can fit this device
        foreach (var universe in universes)
        {
            if (universe.devices == null) continue;
            
            // Use the interface to check if device can fit
            if (layoutStrategy != null && layoutStrategy.CanFitDevice(universe, device))
            {
                // Add device to universe if not already present
                if (!universe.devices.Contains(dmxDevice))
                {
                    var newDevicesList = new List<DMXDevice>(universe.devices ?? new DMXDevice[0]);
                    newDevicesList.Add(dmxDevice);
                    universe.devices = newDevicesList.ToArray();
                }
                
                // Use the interface to assign channels
                layoutStrategy.AssignChannels(universe);
                
                Debug.Log($"Auto-assigned device {dmxDevice.name} to universe {universe.universe} using {layoutStrategy.GetLayoutName()}");
                return;
            }
        }
        
        string strategyName = layoutStrategy?.GetLayoutName() ?? "No Strategy";
        Debug.LogWarning($"Could not auto-assign device {dmxDevice.name} using {strategyName}");
    }
    
    // Reassign method - now uses only the interface
    [ContextMenu("Reassign All Channels")]
    public void ReassignAllChannels()
    {
        if (layoutStrategy == null) return;
        
        foreach (var universe in universes)
        {
            layoutStrategy.AssignChannels(universe);
        }
    }
    
    // Method to change layout at runtime
    public void SetLayoutType(LayoutType type)
    {
        currentLayoutType = type;
        UpdateLayoutStrategy();
        ReassignAllChannels();
    }

    private void OnDestroy()
    {
        artnet.Close();
    }

    private void Update()
    {
        // Handle incoming DMX data
        var keys = dmxDataMap.Keys.ToArray();
        for (var i = 0; i < keys.Length; i++)
        {
            var universe = keys[i];
            var dmxData = dmxDataMap[universe];
            if (dmxData == null)
                continue;

            var universeDevices = universes.FirstOrDefault(u => u.universe == universe);
            if (universeDevices != null)
                foreach (var d in universeDevices.devices)
                    d.SetData(dmxData.Skip(d.StartChannel).Take(d.NumChannels).ToArray());

            dmxDataMap[universe] = null;
        }
        
        // Send batched output data
        if (enableBatchedSending)
        {
            SendBatchedData();
        }
    }

    [ContextMenu("send DMX")]
    public void Send()
    {
        if (useBroadcast && isServer)
            artnet.Send(dmxToSend);
        else
            artnet.Send(dmxToSend, remote);
    }
    
    public void Send(short universe, byte[] dmxData)
    {
        dmxToSend.Universe = universe;
        System.Buffer.BlockCopy(dmxData, 0, dmxToSend.DmxData, 0, dmxData.Length);

        if (useBroadcast && isServer)
            artnet.Send(dmxToSend);
        else
            artnet.Send(dmxToSend, remote);
    }

    static IPAddress FindFromHostName(string hostname)
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
            Debug.LogErrorFormat(
                "Failed to find IP for :\n host name = {0}\n exception={1}",
                hostname, e);
        }
        return address;
    }

    [System.Serializable]
    public class UniverseDevices
    {
        public string universeName;
        public int universe;
        public DMXDevice[] devices;

        public void Initialize()
        {
            if (devices == null) return;
            
            var startChannel = 0;
            foreach (var d in devices)
                if (d != null)
                {
                    d.StartChannel = startChannel;
                    startChannel += d.NumChannels;
                    d.name = string.Format("{0}:({1},{2:d3}-{3:d3})", d.GetType().ToString(), universe, d.StartChannel, startChannel - 1);
                }
            if (512 < startChannel)
                Debug.LogErrorFormat("The number({0}) of channels of the universe {1} exceeds the upper limit(512 channels)!", startChannel, universe);
        }
    }
    
    [Header("Gizmo Configuration")]
    [SerializeField] private bool showUniverseGizmos = true;
    [SerializeField] private float gizmoTextSize = 1f;
    [SerializeField] private Vector3 gizmoOffset = new Vector3(0, 2, 0);
    [SerializeField] private Color universeTextColor = Color.cyan;
    
    // Add gizmo drawing methods
    private void OnDrawGizmos()
    {
        if (showUniverseGizmos && universes != null)
        {
            DrawUniverseGizmos();
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        if (showUniverseGizmos && universes != null)
        {
            DrawUniverseGizmos();
        }
    }
    
    private void DrawUniverseGizmos()
    {
        Vector3 controllerPosition = transform.position + gizmoOffset;
        
        // Draw controller info
        string controllerInfo = $"DMX Controller\nLayout: {layoutStrategy?.GetLayoutName() ?? "None"}";
        DrawGizmoText(controllerPosition, controllerInfo, universeTextColor);
        
        // Draw universe summary
        for (int i = 0; i < universes.Length; i++)
        {
            var universe = universes[i];
            if (universe.devices == null) continue;
            
            Vector3 universePosition = controllerPosition + Vector3.down * (0.5f * gizmoTextSize * (i + 2));
            string universeInfo = GetUniverseInfo(universe);
            DrawGizmoText(universePosition, universeInfo, GetUniverseColor(universe));
        }
    }
    
    private string GetUniverseInfo(UniverseDevices universe)
    {
        int deviceCount = universe.devices?.Length ?? 0;
        int channelCount = GetUniverseChannelCount(universe);
        
        return $"Universe {universe.universe}: {deviceCount} devices, {channelCount}/512 channels";
    }
    
    private int GetUniverseChannelCount(UniverseDevices universe)
    {
        if (universe.devices == null) return 0;
        
        int maxChannel = 0;
        foreach (var device in universe.devices)
        {
            if (device != null)
            {
                maxChannel = Mathf.Max(maxChannel, device.StartChannel + device.NumChannels);
            }
        }
        return maxChannel;
    }
    
    private Color GetUniverseColor(UniverseDevices universe)
    {
        int channelCount = GetUniverseChannelCount(universe);
        
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
}