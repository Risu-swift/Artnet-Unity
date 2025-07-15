using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DrawnLayoutData
{
    public List<Vector2> points = new List<Vector2>();
    public List<DrawnLayoutSegment> segments = new List<DrawnLayoutSegment>();
    public int devicesPerSegment = 1;
    public int channelsPerDevice = 4;
    public int startChannel = 1;
    public int channelSpacing = 4;
    public bool reverseDirection = false;
}

[System.Serializable]
public class DrawnLayoutSegment
{
    public Vector2 startPoint;
    public Vector2 endPoint;
    public int deviceCount;
    public List<Vector2> devicePositions = new List<Vector2>();
    
    public DrawnLayoutSegment(Vector2 start, Vector2 end, int devices)
    {
        startPoint = start;
        endPoint = end;
        deviceCount = devices;
        CalculateDevicePositions();
    }
    
    public void CalculateDevicePositions()
    {
        devicePositions.Clear();
        
        if (deviceCount <= 0) return;
        
        if (deviceCount == 1)
        {
            devicePositions.Add(Vector2.Lerp(startPoint, endPoint, 0.5f));
        }
        else
        {
            for (int i = 0; i < deviceCount; i++)
            {
                float t = (float)i / (deviceCount - 1);
                devicePositions.Add(Vector2.Lerp(startPoint, endPoint, t));
            }
        }
    }
}