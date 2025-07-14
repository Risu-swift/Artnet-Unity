public enum DMXDeviceMode
{
    Input,          // Receive DMX data and apply to GameObject
    Output,         // Read from GameObject and send DMX data
    Bidirectional   // Both input and output
}

public enum ChannelFunction
{
    Unknown = -1,
    None = -2,          // Dummy channel - no data is updated
    Color_R = 0,
    Color_RFine = 1,
    Color_G = 2,
    Color_GFine = 3,
    Color_B = 4,
    Color_BFine = 5,
    Color_W = 6,
    Color_WFine = 7,
    Color_A = 8,        // Alpha channel
    Color_AFine = 9,
    Pan = 10,
    PanFine = 11,
    Tilt = 12,
    TiltFine = 13,
    RotSpeed = 14,
    Intensity = 15,
    IntensityFine = 16,
    Strobe = 17,
    Dimmer = 18
}