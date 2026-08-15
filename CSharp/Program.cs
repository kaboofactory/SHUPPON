using System.Diagnostics;

namespace StarRunnerPrototype;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Priority tuning is best-effort. Never prevent the game from starting.
        }

        CpuEvaluationProfileStorage.LoadIntoProvider();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
