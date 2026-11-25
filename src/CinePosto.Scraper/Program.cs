using System.Runtime.InteropServices;
using CinePosto.Scraper;

// Ctrl+C and SIGTERM (what `docker stop` sends) cancel the run instead of killing it halfway.
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancellation.Cancel();
});

return await ScraperApp.RunAsync(Environment.GetEnvironmentVariable, Console.Out, Console.Error, TimeProvider.System, cancellation.Token);
