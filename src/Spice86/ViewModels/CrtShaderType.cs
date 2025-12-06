namespace Spice86.ViewModels;

/// <summary>
/// Available CRT shader effects
/// </summary>
public enum CrtShaderType {
    /// <summary>
    /// No CRT effect - simple passthrough rendering
    /// </summary>
    None,
    
    /// <summary>
    /// FakeLottes shader - recommended for resolutions up to 640x480
    /// </summary>
    FakeLottes,
    
    /// <summary>
    /// EasyMode shader - recommended for resolutions over 640x480
    /// </summary>
    EasyMode,
    
    /// <summary>
    /// CRT-Geom shader - jack of all trades for all resolutions
    /// </summary>
    CrtGeom
}
