using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class DMXLayoutDrawingWindow : EditorWindow
{
    private DrawnLayoutData layoutData;
    private DmxController targetController;
    private bool isDrawing = false;
    private Vector2 currentMousePos;
    private Vector2 drawStartPos;
    private Vector2 scrollPosition;
    private float zoomLevel = 1f;
    
    // Drawing settings
    private int devicesPerSegment = 1;
    private Color lineColor = Color.white;
    private Color deviceColor = Color.cyan;
    private Color channelTextColor = Color.yellow;
    private bool showChannelNumbers = true;
    private bool showDevicePositions = true;
    
    [MenuItem("DMX/Layout Drawing Window")]
    public static void ShowWindow()
    {
        GetWindow<DMXLayoutDrawingWindow>("DMX Layout Designer");
    }
    
    private void OnEnable()
    {
        layoutData = new DrawnLayoutData();
        
        // Find DMX controller in scene
        targetController = FindObjectOfType<DmxController>();
    }
    
    private void OnGUI()
    {
        DrawToolbar();
        DrawCanvas();
        DrawSettingsPanel();
    }
    
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
        {
            ClearLayout();
        }
        
        if (GUILayout.Button("Apply Layout", EditorStyles.toolbarButton))
        {
            ApplyLayoutToController();
        }
        
        GUILayout.Space(10);
        
        GUILayout.Label("Devices/Segment:", GUILayout.Width(100));
        devicesPerSegment = EditorGUILayout.IntField(devicesPerSegment, GUILayout.Width(50));
        
        GUILayout.Space(10);
        
        GUILayout.Label("Zoom:", GUILayout.Width(40));
        zoomLevel = EditorGUILayout.Slider(zoomLevel, 0.1f, 2f, GUILayout.Width(100));
        
        GUILayout.FlexibleSpace();
        
        if (targetController == null)
        {
            GUILayout.Label("No DMX Controller found in scene", EditorStyles.toolbarButton);
        }
        else
        {
            GUILayout.Label($"Target: {targetController.name}", EditorStyles.toolbarButton);
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawCanvas()
    {
        Rect canvasRect = new Rect(0, 20, position.width - 200, position.height - 20);
        
        // Draw canvas background
        EditorGUI.DrawRect(canvasRect, new Color(0.2f, 0.2f, 0.2f, 1f));
        
        // Handle mouse events
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;
        
        if (canvasRect.Contains(mousePos))
        {
            HandleMouseEvents(e, mousePos, canvasRect);
        }
        
        // Draw with zoom and scroll
        GUI.BeginGroup(canvasRect);
        
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(scrollPosition, Quaternion.identity, Vector3.one * zoomLevel);
        
        DrawLayoutSegments();
        DrawDevicePositions();
        
        // Draw current line being drawn
        if (isDrawing)
        {
            DrawLine(drawStartPos, currentMousePos, Color.gray, 2f);
        }
        
        GUI.matrix = oldMatrix;
        GUI.EndGroup();
    }
    
    private void HandleMouseEvents(Event e, Vector2 mousePos, Rect canvasRect)
    {
        Vector2 localMousePos = (mousePos - canvasRect.position - scrollPosition) / zoomLevel;
        currentMousePos = localMousePos;
        
        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0) // Left click
                {
                    isDrawing = true;
                    drawStartPos = localMousePos;
                    e.Use();
                }
                break;
                
            case EventType.MouseUp:
                if (e.button == 0 && isDrawing)
                {
                    isDrawing = false;
                    AddSegment(drawStartPos, localMousePos);
                    e.Use();
                }
                break;
                
            case EventType.MouseDrag:
                if (e.button == 1) // Right click drag for panning
                {
                    scrollPosition += e.delta;
                    e.Use();
                }
                break;
                
            case EventType.ScrollWheel:
                float zoomDelta = -e.delta.y * 0.1f;
                zoomLevel = Mathf.Clamp(zoomLevel + zoomDelta, 0.1f, 2f);
                e.Use();
                break;
        }
        
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            Repaint();
        }
    }
    
    private void AddSegment(Vector2 start, Vector2 end)
    {
        if (Vector2.Distance(start, end) < 10f) return; // Minimum line length
        
        var segment = new DrawnLayoutSegment(start, end, devicesPerSegment);
        layoutData.segments.Add(segment);
        
        UpdateLayoutData();
        Repaint();
    }
    
    private void UpdateLayoutData()
    {
        layoutData.devicesPerSegment = devicesPerSegment;
        
        // Recalculate all device positions
        foreach (var segment in layoutData.segments)
        {
            segment.deviceCount = devicesPerSegment;
            segment.CalculateDevicePositions();
        }
    }
    
    private void DrawLayoutSegments()
    {
        foreach (var segment in layoutData.segments)
        {
            DrawLine(segment.startPoint, segment.endPoint, lineColor, 3f);
        }
    }
    
    private void DrawDevicePositions()
    {
        if (!showDevicePositions) return;
        
        int deviceIndex = 0;
        int currentChannel = layoutData.startChannel;
        
        foreach (var segment in layoutData.segments)
        {
            foreach (var devicePos in segment.devicePositions)
            {
                // Draw device circle
                DrawCircle(devicePos, 8f, deviceColor);
                
                // Draw channel number
                if (showChannelNumbers)
                {
                    string channelText = $"{currentChannel}";
                    DrawText(devicePos + Vector2.up * 15, channelText, channelTextColor);
                }
                
                currentChannel += layoutData.channelsPerDevice;
                deviceIndex++;
            }
        }
    }
    
    private void DrawLine(Vector2 start, Vector2 end, Color color, float width)
    {
        Handles.BeginGUI();
        Handles.color = color;
        Handles.DrawLine(start, end);
        Handles.EndGUI();
    }
    
    private void DrawCircle(Vector2 center, float radius, Color color)
    {
        Handles.BeginGUI();
        Handles.color = color;
        Handles.DrawWireDisc(center, Vector3.forward, radius);
        Handles.EndGUI();
    }
    
    private void DrawText(Vector2 position, string text, Color color)
    {
        GUI.color = color;
        GUI.Label(new Rect(position.x - 10, position.y - 10, 20, 20), text);
        GUI.color = Color.white;
    }
    
    private void DrawSettingsPanel()
    {
        Rect settingsRect = new Rect(position.width - 200, 20, 200, position.height - 20);
        
        GUILayout.BeginArea(settingsRect);
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("Layout Settings", EditorStyles.boldLabel);
        
        layoutData.startChannel = EditorGUILayout.IntField("Start Channel", layoutData.startChannel);
        layoutData.channelsPerDevice = EditorGUILayout.IntField("Channels/Device", layoutData.channelsPerDevice);
        layoutData.channelSpacing = EditorGUILayout.IntField("Channel Spacing", layoutData.channelSpacing);
        
        GUILayout.Space(10);
        
        GUILayout.Label("Display Options", EditorStyles.boldLabel);
        showDevicePositions = EditorGUILayout.Toggle("Show Devices", showDevicePositions);
        showChannelNumbers = EditorGUILayout.Toggle("Show Channels", showChannelNumbers);
        
        GUILayout.Space(10);
        
        GUILayout.Label("Colors", EditorStyles.boldLabel);
        lineColor = EditorGUILayout.ColorField("Line Color", lineColor);
        deviceColor = EditorGUILayout.ColorField("Device Color", deviceColor);
        channelTextColor = EditorGUILayout.ColorField("Text Color", channelTextColor);
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Update Layout"))
        {
            UpdateLayoutData();
        }
        
        GUILayout.Space(10);
        
        GUILayout.Label("Instructions", EditorStyles.boldLabel);
        GUILayout.TextArea("• Left click and drag to draw lines\n• Right click and drag to pan\n• Scroll to zoom\n• Each line represents a segment with devices", EditorStyles.wordWrappedLabel);
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    private void ClearLayout()
    {
        layoutData.segments.Clear();
        Repaint();
    }
    
    private void ApplyLayoutToController()
    {
        if (targetController == null)
        {
            EditorUtility.DisplayDialog("Error", "No DMX Controller found in scene!", "OK");
            return;
        }
        
        // Apply to controller
        targetController.ApplyDrawnLayout(layoutData);
        
        Debug.Log($"Applied drawn layout with {layoutData.segments.Count} segments");
        EditorUtility.DisplayDialog("Layout Applied", $"Applied layout with {layoutData.segments.Count} segments", "OK");
    }
}