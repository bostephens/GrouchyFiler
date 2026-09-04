using System.Security.Principal;

namespace GrouchyFiler.Services;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    internal bool IsPrimary { get; }

    internal SingleInstance(string? name = null)
    {
        name ??= @"Local\GrouchyFiler-" + WindowsIdentity.GetCurrent().User!.Value;
        // Create the activation event first so launches during startup cannot lose their signal.
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Activate");
        mutex = new Mutex(false, name + "-Instance");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (!IsPrimary) activation.Set();
    }
    internal bool TakeActivation() => activation.WaitOne(0);
    public void Dispose()
    {
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
        activation.Dispose();
    }
}


