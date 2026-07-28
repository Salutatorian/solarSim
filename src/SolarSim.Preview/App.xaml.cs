using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SolarSim.Preview;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            var detail = FormatException(e.Exception);
            TryWriteCrashLog(detail);
            MessageBox.Show(
                $"solarSim hit an error and recovered:\n\n{InnermostMessage(e.Exception)}\n\n" +
                $"({RootTypeName(e.Exception)})\n\nDetails saved to:\n{CrashLogPath()}",
                "solarSim",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                var detail = FormatException(ex);
                TryWriteCrashLog(detail);
                MessageBox.Show(
                    $"solarSim crashed:\n\n{InnermostMessage(ex)}\n\nDetails saved to:\n{CrashLogPath()}",
                    "solarSim",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
    }

    private static string CrashLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "solarSim",
            "last-error.log");

    private static void TryWriteCrashLog(string detail)
    {
        try
        {
            var path = CrashLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{detail}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static string InnermostMessage(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex.Message;
    }

    private static string RootTypeName(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex.GetType().Name;
    }

    private static string FormatException(Exception ex)
    {
        var sb = new StringBuilder();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            sb.AppendLine($"{current.GetType().Name}: {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
                sb.AppendLine(current.StackTrace);
            sb.AppendLine("---");
        }
        return sb.ToString();
    }
}
