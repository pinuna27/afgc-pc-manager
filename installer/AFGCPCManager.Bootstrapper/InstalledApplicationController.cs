using System.Diagnostics;
using AFGCPCManager.HidHide;
using AFGCPCManager.Setup.Core;

namespace AFGCPCManager.Bootstrapper;

internal interface IInstalledApplicationController
{
    Task StopAsync(
        string destination,
        Action<string> report,
        CancellationToken cancellationToken = default);
    void StartUnelevated(string destination);
}

internal sealed class InstalledApplicationController(
    TimeProvider? timeProvider = null) : IInstalledApplicationController
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task StopAsync(
        string destination,
        Action<string> report,
        CancellationToken cancellationToken = default)
    {
        string applicationPath = Path.Combine(destination, "AFGCPCManager.exe");
        if (!File.Exists(applicationPath))
            return;

        using Process? exitRequest = Process.Start(new ProcessStartInfo(
            applicationPath, "--exit")
        { UseShellExecute = false });
        if (exitRequest is not null)
        {
            using var exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            exitTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await exitRequest.WaitForExitAsync(exitTimeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }

        DateTimeOffset deadline = _timeProvider.GetUtcNow().AddSeconds(15);
        while (_timeProvider.GetUtcNow() < deadline)
        {
            using ProcessCollection running = FindProcesses(applicationPath);
            if (running.Count == 0)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(200), _timeProvider,
                cancellationToken);
        }

        report("The previous AFGC PC Manager version did not exit; "
            + "completing a safe forced shutdown...");
        using (ProcessCollection running = FindProcesses(applicationPath))
        {
            foreach (Process process in running.Processes)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException
                                           or System.ComponentModel.Win32Exception)
                {
                    throw new InvalidOperationException(
                        $"Could not stop the previous AFGC PC Manager process {process.Id}.", ex);
                }
            }

            using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            stopTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            foreach (Process process in running.Processes)
            {
                try { await process.WaitForExitAsync(stopTimeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "The previous AFGC PC Manager process could not be stopped safely.");
                }
            }
        }

        string journalPath = Path.Combine(
            SetupProductIdentity.LocalDataDirectory, "hidhide-journal.json");
        await new HidHideService(new DeviceInstanceResolver(),
                new HidHideJournalStore(journalPath))
            .RecoverOwnedEntriesAsync(cancellationToken);

        DateTimeOffset finalDeadline = _timeProvider.GetUtcNow().AddSeconds(5);
        while (_timeProvider.GetUtcNow() < finalDeadline)
        {
            using ProcessCollection remaining = FindProcesses(applicationPath);
            if (remaining.Count == 0)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100), _timeProvider,
                cancellationToken);
        }
        throw new TimeoutException(
            "AFGC PC Manager remained active after forced shutdown.");
    }

    public void StartUnelevated(string destination)
    {
        string applicationPath = Path.Combine(destination, "AFGCPCManager.exe");
        if (!File.Exists(applicationPath))
            return;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add(applicationPath);
        Process.Start(start);
    }

    private static ProcessCollection FindProcesses(string applicationPath)
    {
        var matches = new List<Process>();
        foreach (Process candidate in Process.GetProcessesByName("AFGCPCManager"))
        {
            try
            {
                if (string.Equals(candidate.MainModule?.FileName, applicationPath,
                        StringComparison.OrdinalIgnoreCase))
                    matches.Add(candidate);
                else
                    candidate.Dispose();
            }
            catch
            {
                // The process can exit between enumeration and reading MainModule
                // after the cooperative --exit request.
                try
                {
                    if (candidate.HasExited)
                    {
                        candidate.Dispose();
                        continue;
                    }
                }
                catch (InvalidOperationException)
                {
                    candidate.Dispose();
                    continue;
                }
                candidate.Dispose();
                throw new InvalidOperationException(
                    "Setup could not verify the path of a running AFGC PC Manager process.");
            }
        }
        return new(matches);
    }

    private sealed class ProcessCollection(List<Process> processes) : IDisposable
    {
        public IReadOnlyList<Process> Processes { get; } = processes;
        public int Count => Processes.Count;

        public void Dispose()
        {
            foreach (Process process in Processes)
                process.Dispose();
        }
    }
}
