using System.Windows;
using System.Windows.Controls;

namespace SolarSim.Preview;

internal interface IAppModal
{
    event Action<bool?> Completed;
}

/// <summary>
/// Shows dialogs inside MainWindow so window-capture recorders keep the same HWND.
/// </summary>
internal static class AppModalHost
{
    public static MainWindow? Main =>
        System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
        ?? System.Windows.Application.Current?.MainWindow as MainWindow;

    public static bool? Show(IAppModal content)
    {
        if (content is not FrameworkElement)
            throw new ArgumentException("Modal must be a FrameworkElement.", nameof(content));
        var main = Main;
        if (main is null)
            return null;
        return main.ShowAppModal(content);
    }
}

internal sealed class CodeModal : UserControl, IAppModal
{
    public event Action<bool?>? Completed;

    public void Complete(bool? result) => Completed?.Invoke(result);
}
