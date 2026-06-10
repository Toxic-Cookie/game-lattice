namespace Lattice.Core.Hosting.Standalone;

/// <summary>Plain-console logger for the standalone host.</summary>
public sealed class ConsoleLogger : ILatticeLogger
{
    private readonly LogLevel _minimum;

    public ConsoleLogger(LogLevel minimum = LogLevel.Info)
    {
        _minimum = minimum;
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minimum)
        {
            return;
        }

        Console.WriteLine($"[{level,-7}] {message}");
    }
}
