public enum DMXDeviceMode
{
    Input,          // Receive DMX data and apply to GameObject
    Output,         // Read from GameObject and send DMX data
    Bidirectional   // Both input and output
}

public enum ChannelFunction
{
    Unknown = -1,
    Color_R = 0,
    Color_RFine = 1,
    Color_G = 2,
    Color_GFine = 3,
    Color_B = 4,
    Color_BFine = 5,
    Color_W = 6,
    Color_WFine = 7,
    Pan = 8,
    PanFine = 9,
    Tilt = 10,
    TiltFine = 11,
    RotSpeed = 12,
    Intensity = 13,
    IntensityFine = 14,
    Strobe = 15,
    Dimmer = 16
}