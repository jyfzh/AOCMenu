// Copyright (c) AOC UI. All rights reserved.

using Microsoft.Windows.ApplicationModel.DynamicDependency;
using WinRT;

namespace aoc.UI;

/// <summary>
/// Program class — application entry point with Windows App SDK bootstrap.
///
/// The bootstrap call (Bootstrap.Initialize) before Application.Start ensures
/// the correct Windows App Runtime framework package is loaded, so the native
/// Microsoft.UI.Xaml.dll resolves properly. Without it, the DLL search path
/// may find an incompatible copy (e.g. from PowerToys), causing a FailFast
/// crash during StartDesktop when it activates IDispatcherQueueControllerStatics.
/// </summary>
public static class Program
{
    [global::System.STAThreadAttribute]
    static void Main(string[] args)
    {
        // ── Initialize Windows App SDK Bootstrap ───────────────────
        // This locates and activates the correct Windows App Runtime
        // framework package (version 2.x), making all native WinUI DLLs
        // available from the framework package rather than relying on
        // the loader's DLL search path.
        Bootstrap.Initialize(0x00020000);

        try
        {
            global::WinRT.ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        finally
        {
            // Shutdown the bootstrap when the application exits.
            Bootstrap.Shutdown();
        }
    }
}
