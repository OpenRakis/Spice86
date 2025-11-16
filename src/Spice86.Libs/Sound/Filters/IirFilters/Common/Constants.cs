namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

/// <summary>
/// Constants used by IIR filters.
/// </summary>
internal static class Constants {
    /// <summary>
    /// The default filter order.
    /// </summary>
    internal const int DefaultFilterOrder = 4;
    
    /// <summary>
    /// Error message for when the requested filter order is too high.
    /// </summary>
    internal const string OrderTooHigh = "Requested order is too high. Provide a higher order for the template.";
}