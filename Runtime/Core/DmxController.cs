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
        }
    }
    
    public void UnregisterDevice(IDMXDevice device)
    {
        registeredDevices.Remove(device);
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

    private void OnValidate()
    {
        if (universes != null)
        {
            foreach (var u in universes)
                u.Initialize();
        }
    }

    public bool newPacket;
    void Start()
    {
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
}