namespace Spice86.Core.CLI;

using System.Diagnostics;

/// <summary>
/// Parses the command line options to create a <see cref="Configuration"/>.
/// </summary>
public interface ICommandLineParser {
    /// <summary>
    /// Parses the command line into a <see cref="Configuration"/> object.
    /// </summary>
    /// <param name="args">The application command line arguments</param>
    /// <returns>A <see cref="Configuration"/> object representing the command line arguments</returns>
    /// <exception cref="UnreachableException">When the command line arguments are unrecognized.</exception>
    public Configuration ParseCommandLine(string[] args);
}
