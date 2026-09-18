using System.Runtime.ExceptionServices;

namespace Ream.Tests;

/// <summary>WPF documents and clipboard objects need a single-threaded apartment; xUnit runs tests on MTA threads.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
