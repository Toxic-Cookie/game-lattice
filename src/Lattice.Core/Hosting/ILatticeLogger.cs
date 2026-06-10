namespace Lattice.Core.Hosting;

/// <summary>Severity levels for framework log output.</summary>
public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Logging seam provided by the host (console, Unity Debug.Log, Godot GD.Print, ...).
/// The framework never writes to a console directly.
/// </summary>
public interface ILatticeLogger
{
    void Log(LogLevel level, string message);
}

/// <summary>Convenience extensions so call sites stay terse.</summary>
public static class LatticeLoggerExtensions
{
    public static void Trace(this ILatticeLogger logger, string message) => logger.Log(LogLevel.Trace, message);

    public static void Debug(this ILatticeLogger logger, string message) => logger.Log(LogLevel.Debug, message);

    public static void Info(this ILatticeLogger logger, string message) => logger.Log(LogLevel.Info, message);

    public static void Warning(this ILatticeLogger logger, string message) => logger.Log(LogLevel.Warning, message);

    public static void Error(this ILatticeLogger logger, string message) => logger.Log(LogLevel.Error, message);
}
