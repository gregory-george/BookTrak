using System.Windows.Forms;
using BookTrak.Audnexus;
using BookTrak.Components;
using BookTrak.Data;
using BookTrak.Hosting;
using BookTrak.OpenLibrary;
using BookTrak.Services;
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
        var shutdownCts = new CancellationTokenSource();
        var hostStopped = new ManualResetEventSlim(false);
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedShutdown = false;

        TrayApplicationContext? tray = null;

        var hostThread = new Thread(() => RunHost(port, contactInfo, inFlightOps, shutdownCts.Token, ready))
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
            shutdownCts.Cancel();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                hostStopped.Wait(TimeSpan.FromSeconds(15));
                tray?.Shutdown();
            });
        });

        Application.Run(tray);
    }

    /// <summary>Runs the Blazor/Kestrel host to completion. Intended to run on a background thread.</summary>
    private static void RunHost(int port, string contactInfo, InFlightOperationCounter inFlightOps, CancellationToken shutdownToken, TaskCompletionSource<bool> ready)
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            builder.Services.AddSingleton(inFlightOps);
            builder.Services.AddDbContextFactory<BookTrakContext>(options =>
                options.UseSqlite($"Data Source={AppPaths.DatabaseFile}")
                    .AddInterceptors(new SqlitePragmaInterceptor()));
            builder.Services.AddOpenLibraryServices(contactInfo);
            builder.Services.AddAudnexusServices(contactInfo);
            builder.Services.AddScoped<ILibraryQueryService, LibraryQueryService>();
            builder.Services.AddScoped<ILibraryWriteService, LibraryWriteService>();
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            var app = builder.Build();

            DatabaseStartup.BackupAndMigrate(app.Services.GetRequiredService<IDbContextFactory<BookTrakContext>>());

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
