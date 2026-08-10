namespace AFGCPCManager.HidHide.Tests;

public sealed class HidHideJournalStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-hidhide-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RoundTripRestoresCaseInsensitiveOwnershipSets()
    {
        string path = Path.Combine(_root, "journal.json");
        var store = new HidHideJournalStore(path);
        var journal = new HidHideJournal
        {
            AddedApplicationPaths = new(StringComparer.OrdinalIgnoreCase) { @"C:\Apps\AFGC.exe" },
            AddedDeviceInstanceIds = new()
            {
                ["controller"] = new(StringComparer.OrdinalIgnoreCase) { "HID\\INSTANCE" }
            },
            PendingHandleResetControllerIds = new(StringComparer.Ordinal) { "controller" },
            HandleResetDisconnectedControllerIds = new(StringComparer.Ordinal) { "controller" }
        };

        await store.SaveAsync(journal, TestContext.Current.CancellationToken);
        HidHideJournal loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(@"c:\apps\afgc.exe", loaded.AddedApplicationPaths);
        Assert.Contains("hid\\instance", loaded.AddedDeviceInstanceIds["controller"]);
        Assert.Contains("controller", loaded.PendingHandleResetControllerIds);
        Assert.Contains("controller", loaded.HandleResetDisconnectedControllerIds);
    }

    [Fact]
    public async Task VersionOneActiveJournalMigratesOwnedControllersToPendingHandleReset()
    {
        string path = Path.Combine(_root, "journal.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, """
            {
              "SchemaVersion": 1,
              "AddedApplicationPaths": ["C:\\\\Apps\\\\AFGC.exe"],
              "AddedDeviceInstanceIds": { "controller": ["HID\\\\INSTANCE"] },
              "ActivatedByApplication": true
            }
            """, TestContext.Current.CancellationToken);

        HidHideJournal loaded = await new HidHideJournalStore(path)
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Contains("controller", loaded.PendingHandleResetControllerIds);
        Assert.Empty(loaded.HandleResetDisconnectedControllerIds);
    }

    [Fact]
    public async Task LoadsBackupWhenCurrentJournalIsCorrupt()
    {
        string path = Path.Combine(_root, "journal.json");
        var store = new HidHideJournalStore(path);
        await store.SaveAsync(new HidHideJournal
        {
            AddedApplicationPaths = new(StringComparer.OrdinalIgnoreCase) { @"C:\first.exe" }
        }, TestContext.Current.CancellationToken);
        await store.SaveAsync(new HidHideJournal
        {
            AddedApplicationPaths = new(StringComparer.OrdinalIgnoreCase) { @"C:\second.exe" }
        }, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "{ invalid", TestContext.Current.CancellationToken);

        HidHideJournal recovered = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(@"C:\first.exe", recovered.AddedApplicationPaths);
    }

    [Fact]
    public async Task RejectsInvalidJournalBeforeSaving()
    {
        var store = new HidHideJournalStore(Path.Combine(_root, "journal.json"));
        var invalid = new HidHideJournal { AddedDeviceInstanceIds = null! };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(invalid, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
