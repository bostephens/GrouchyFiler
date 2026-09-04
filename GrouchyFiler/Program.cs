using GrouchyFiler.Services;

namespace GrouchyFiler;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var instance = new SingleInstance();
        if (!instance.IsPrimary) return;
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        using var activationTimer = new System.Windows.Forms.Timer { Interval = 200 };
        activationTimer.Tick += (_, _) => { if (instance.TakeActivation()) form.ShowMainWindow(); };
        activationTimer.Start();
        Application.Run(form);
    }
}
