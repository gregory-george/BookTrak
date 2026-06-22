using System.Windows.Forms;
using BookTrak.Audnexus;
using BookTrak.Components;
using BookTrak.Data;
using BookTrak.Hosting;
using BookTrak.OpenLibrary;
using BookTrak.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace BookTrak;

public class Program
{
    private const string MutexName = "Global\\BookTrak.SingleInstance.Mutex";

    [STAThread]
    public static void Main(string[] args)
    {
        AppPaths.EnsureDirectories();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            // Another instance owns the mutex. Give its lockfile a moment to be written if it
            // just started, then reopen the browser at its port instead of starting a second copy.
            Thread.Sleep(250);
            var existing = LockInfo.TryRead();
            if (existing is not null && InstanceLock.IsLive(existing))
            {
                BrowserLauncher.Open(existing.Port);
            }

            return;
        }

        // We own the mutex. A lockfile left behind by a crashed process is stale — clear it.
        var leftover = LockInfo.TryRead();
        if (leftover is not null)
        {
            if (InstanceLock.IsLive(leftover))
            {
                // Shouldn't happen if the mutex was free, but don't start a second instance.
                BrowserLauncher.Open(leftover.Port);
                return;
            }

            InstanceLock.Delete();
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = AppConfig.LoadOrCreate();
        var port = PortFinder.FindFreePort(config.Port);
        if (port != config.Port)
        {
            config.Port = port;
            config.Save();
        }

        InstanceLock.Write(port, Environment.ProcessId);

        try
        {
            RunApplication(port, config.ContactInfo);
        }
        finally
        {
            InstanceLock.Delete();
        }
    }

    private static void RunApplication(int port, string contactInfo)
    {
        var inFlightOps = new InFlightOperationCounter();
        var circuitCounter = new CircuitCounter();
        var shutdownCts = new CancellationTokenSource();
        var hostStopped = new ManualResetEventSlim(false);
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedShutdown = false;

        TrayApplicationContext? tray = null;

        var hostThread = new Thread(() => RunHost(port, contactInfo, inFlightOps, circuitCounter, shutdownCts.Token, ready))
        {
            IsBackground = true,
            Name = "BookTrak-Host",
        };
        hostThread.Start();

        // The host thread always sets hostStopped, whether it stopped cleanly or crashed.
        var watcher = new Thread(() =>
        {
            hostThread.Join();
            hostStopped.Set();
            if (!expectedShutdown)
            {
                tray?.OnHostCrashed();
            }
        })
        {
            IsBackground = true,
            Name = "BookTrak-HostWatcher",
        };
        watcher.Start();

        bool started;
        try
        {
            started = ready.Task.Wait(TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            started = false;
        }

        if (!started || ready.Task.IsFaulted)
        {
            var message = ready.Task.IsFaulted
                ? ready.Task.Exception?.GetBaseException().Message
                : "Timed out waiting for the local server to start.";
            MessageBox.Show($"BookTrak failed to start:\n{message}", "BookTrak", MessageBoxButtons.OK, MessageBoxIcon.Error);
            expectedShutdown = true;
            shutdownCts.Cancel();
            return;
        }

        BrowserLauncher.Open(port);

        tray = new TrayApplicationContext(port, requestShutdown: () =>
        {
            expectedShutdown = true;
            ThreadPool.QueueUserWorkItem(async _ => await RunShutdownSequenceAsync(tray, inFlightOps, circuitCounter, shutdownCts, hostStopped).ConfigureAwait(false));
        });

        Application.Run(tray);
    }

    /// <summary>"Finish, then exit": stop accepting new work and wait for zero in-flight
    /// background ops (OL/audnexus calls, cover fetches) AND zero connected UI circuits before
    /// actually stopping Kestrel. Surfaces progress in the tray tooltip. A fallback deadline keeps
    /// a stuck/never-disconnecting circuit from hanging shutdown forever.</summary>
    private static async Task RunShutdownSequenceAsync(TrayApplicationContext? tray, InFlightOperationCounter inFlightOps, CircuitCounter circuitCounter, CancellationTokenSource shutdownCts, ManualResetEventSlim hostStopped)
    {
        await ShutdownCoordinator.WaitForIdleAsync(
            getInFlightOps: () => inFlightOps.Count,
            getConnectedCircuits: () => circuitCounter.Count,
            onWaiting: (ops, circuits) => tray?.UpdateTooltip(BuildShutdownTooltip(ops, circuits)),
            timeout: TimeSpan.FromSeconds(60),
            pollInterval: TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

        shutdownCts.Cancel();
        hostStopped.Wait(TimeSpan.FromSeconds(15));
        tray?.Shutdown();
    }

    private static string BuildShutdownTooltip(int inFlightOps, int connectedCircuits)
    {
        var parts = new List<string>();
        if (inFlightOps > 0)
        {
            parts.Add($"{inFlightOps} download{(inFlightOps == 1 ? "" : "s")}");
        }

        if (connectedCircuits > 0)
        {
            parts.Add($"{connectedCircuits} open tab{(connectedCircuits == 1 ? "" : "s")}");
        }

        return $"BookTrak — finishing {string.Join(", ", parts)}…";
    }

    /// <summary>Runs the Blazor/Kestrel host to completion. Intended to run on a background thread.</summary>
    private static void RunHost(int port, string contactInfo, InFlightOperationCounter inFlightOps, CircuitCounter circuitCounter, CancellationToken shutdownToken, TaskCompletionSource<bool> ready)
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            builder.Services.AddSingleton(inFlightOps);
            builder.Services.AddSingleton(circuitCounter);
            builder.Services.AddScoped<CircuitHandler, TrackingCircuitHandler>();
            builder.Services.AddDbContextFactory<BookTrakContext>(options =>
                options.UseSqlite($"Data Source={AppPaths.DatabaseFile}")
                    .AddInterceptors(new SqlitePragmaInterceptor()));
            builder.Services.AddOpenLibraryServices(contactInfo);
            builder.Services.AddAudnexusServices(contactInfo);
            builder.Services.AddScoped<ILibraryQueryService, LibraryQueryService>();
            builder.Services.AddScoped<ILibraryWriteService, LibraryWriteService>();
            builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
            builder.Services.AddScoped<IImportService, ImportService>();
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            var app = builder.Build();

            var dbContextFactory = app.Services.GetRequiredService<IDbContextFactory<BookTrakContext>>();
            DatabaseStartup.BackupAndMigrate(dbContextFactory);
            OrphanCoverCleanup.SweepAsync(dbContextFactory).GetAwaiter().GetResult();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseAntiforgery();

            // Covers live in covers/ next to the .exe, outside wwwroot — served separately so
            // the published single-file app doesn't need them bundled as static web assets.
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(AppPaths.CoversDirectory),
                RequestPath = "/covers",
            });

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Lifetime.ApplicationStarted.Register(() => ready.TrySetResult(true));

            app.RunAsync(shutdownToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
    }
}
