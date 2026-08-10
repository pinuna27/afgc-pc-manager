namespace AFGCPCManager.HidHide.Tests;

public sealed class HidHideServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-hidhide-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task AddsOnlyMissingEntriesAndActivatesHiding()
    {
        string app = Path.Combine(_root, "AFGCPCManager.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi(); api.Blocked.Add("existing"); var service = Create(api, new Dictionary<string, string> { ["one"] = "existing", ["two"] = "new" });
        await service.HideAsync("controller", ["one", "two"], app, TestContext.Current.CancellationToken);
        Assert.True(api.IsActive); Assert.Contains(Path.GetFullPath(app), api.Apps, StringComparer.OrdinalIgnoreCase); Assert.Contains("new", api.Blocked); Assert.Equal(2, api.Blocked.Count);
    }
    [Fact]
    public async Task RecoveryPreservesPreexistingEntries()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi(); api.Blocked.Add("other"); api.Apps.Add("C:\\Other.exe"); var service = Create(api, new() { ["path"] = "owned" });
        await service.HideAsync("controller", ["path"], app, TestContext.Current.CancellationToken); await service.RecoverOwnedEntriesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["other"], api.Blocked); Assert.Equal(["C:\\Other.exe"], api.Apps);
        Assert.False(api.IsActive);
    }
    [Fact]
    public async Task UnhideRemovesOnlySpecifiedControllersEntries()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test"); var api = new FakeApi(); var service = Create(api, new() { ["a"] = "id-a", ["b"] = "id-b" });
        await service.HideAsync("a", ["a"], app, TestContext.Current.CancellationToken); await service.HideAsync("b", ["b"], app, TestContext.Current.CancellationToken); await service.UnhideAsync("a", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("id-a", api.Blocked); Assert.Contains("id-b", api.Blocked);
    }
    [Fact]
    public async Task UnhidingLastControllerRestoresApplicationAndActivationState()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi(); var service = Create(api, new() { ["path"] = "owned" });

        await service.HideAsync("controller", ["path"], app, TestContext.Current.CancellationToken);
        await service.UnhideAsync("controller", TestContext.Current.CancellationToken);

        Assert.False(api.IsActive);
        Assert.Empty(api.Apps);
        Assert.Empty(api.Blocked);
        HidHideJournal journal = await new HidHideJournalStore(Path.Combine(_root, "journal.json"))
            .LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(journal.ActivatedByApplication);
    }
    [Fact]
    public async Task RecoveryPreservesPreexistingActiveState()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi { IsActive = true }; var service = Create(api, new() { ["path"] = "owned" });

        await service.HideAsync("controller", ["path"], app, TestContext.Current.CancellationToken);
        await service.RecoverOwnedEntriesAsync(TestContext.Current.CancellationToken);

        Assert.True(api.IsActive);
    }
    [Fact]
    public async Task RecoveryCanRetryAfterApiRemovalFailure()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi(); var service = Create(api, new() { ["path"] = "owned" });
        await service.HideAsync("controller", ["path"], app, TestContext.Current.CancellationToken);
        api.ThrowOnRemoveBlockedOnce = true;

        await Assert.ThrowsAsync<IOException>(() => service.RecoverOwnedEntriesAsync(TestContext.Current.CancellationToken));
        await service.RecoverOwnedEntriesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(api.Blocked);
        Assert.Empty(api.Apps);
        Assert.False(api.IsActive);
    }
    [Fact]
    public async Task RejectsEmptyControllerPathsBeforeChangingHidHide()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi(); var service = Create(api, []);

        await Assert.ThrowsAsync<ArgumentException>(() => service.HideAsync(
            "controller", [], app, TestContext.Current.CancellationToken));

        Assert.Empty(api.Apps);
        Assert.Empty(api.Blocked);
        Assert.False(api.IsActive);
    }
    [Fact]
    public async Task HideAndVerifyRequiresIndependentProbeConfirmation()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi();
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Hidden, "confirmed"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(api, new() { ["path"] = "owned" }, verifier);

        bool reconnectRequired = await service.HideAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.True(reconnectRequired);
        Assert.Equal(HidHideVisibilityStatus.Hidden, result.Status);
        Assert.Equal(1, verifier.Calls);
        Assert.Contains("owned", api.Blocked);
        Assert.Contains(Path.GetFullPath(app), api.Apps);
        Assert.True(api.IsActive);
    }
    [Fact]
    public async Task NewHidingConfigurationIsVerifiedImmediately()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Hidden, "confirmed"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(new FakeApi(), new() { ["path"] = "owned" }, verifier);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.Equal(HidHideVisibilityStatus.Hidden, result.Status);
        Assert.True(result.HandleResetRequired);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task AcknowledgedHandleResetDoesNotReturnForUnchangedConfiguration()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Hidden, "confirmed"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(new FakeApi(), new() { ["path"] = "owned" }, verifier);

        HidHideVisibilityResult first = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);
        await service.AcknowledgeHandleResetAsync(
            "controller", TestContext.Current.CancellationToken);
        HidHideVisibilityResult second = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.True(first.HandleResetRequired);
        Assert.False(second.HandleResetRequired);
        Assert.Equal(HidHideVisibilityStatus.Hidden, second.Status);
    }

    [Fact]
    public async Task PrepareOwnedEntriesPreservesRulesAndPendingResetAcrossRelaunch()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi();
        HidHideService first = Create(api, new() { ["path"] = "owned" });
        await first.HideAsync("controller", ["path"], app, TestContext.Current.CancellationToken);
        await first.MarkHandleResetDisconnectedAsync(
            "controller", TestContext.Current.CancellationToken);

        HidHideService relaunched = Create(api, new() { ["path"] = "owned" });
        HidHideOwnedState owned = await relaunched.PrepareOwnedEntriesAsync(
            app, TestContext.Current.CancellationToken);

        Assert.Contains("controller", owned.ControllerIds);
        Assert.Contains("controller", owned.PendingHandleResetControllerIds);
        Assert.Contains("controller", owned.HandleResetDisconnectedControllerIds);
        Assert.Contains("owned", api.Blocked);
        Assert.True(api.IsActive);
    }

    [Fact]
    public async Task NewHidingConfigurationStillFailsClosedWhenControllerRemainsVisible()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var verifier = new FakeVisibilityVerifier(new(
            HidHideVisibilityStatus.Visible, "physical controller remains visible"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(new FakeApi(), new() { ["path"] = "owned" }, verifier);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.Equal(HidHideVisibilityStatus.Visible, result.Status);
        Assert.Equal("physical controller remains visible", result.Detail);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task HideAndVerifyFailsClosedWhenConfigurationReadbackDoesNotMatch()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi { IgnoreBlockedAdds = true };
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Hidden, "confirmed"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(api, new() { ["path"] = "owned" }, verifier);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.Equal(HidHideVisibilityStatus.Indeterminate, result.Status);
        Assert.Equal(0, verifier.Calls);
    }
    [Fact]
    public async Task HideAndVerifyRejectsWhitelistedProbeWithoutRemovingUserEntry()
    {
        string app = Path.Combine(_root, "app.exe"); string probePath = Path.Combine(_root, "probe.exe");
        Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi(); api.Apps.Add(Path.GetFullPath(probePath));
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Hidden, "confirmed"), probePath);
        HidHideService service = Create(api, new() { ["path"] = "owned" }, verifier);

        await service.HideAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.Equal(HidHideVisibilityStatus.Indeterminate, result.Status);
        Assert.Contains(Path.GetFullPath(probePath), api.Apps);
        Assert.Equal(0, verifier.Calls);
    }
    [Fact]
    public async Task HideAndVerifyReportsPhysicalControllerStillVisible()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Visible, "reconnect"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(new FakeApi(), new() { ["path"] = "owned" }, verifier);

        await service.HideAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.Equal(HidHideVisibilityStatus.Visible, result.Status);
        Assert.Equal("reconnect", result.Detail);
    }
    [Fact]
    public async Task HideAndVerifyAcceptsDriverNamespaceApplicationReadback()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test");
        var api = new FakeApi { ReturnApplicationPathsInDriverNamespace = true };
        var verifier = new FakeVisibilityVerifier(new(HidHideVisibilityStatus.Hidden, "confirmed"),
            Path.Combine(_root, "probe.exe"));
        HidHideService service = Create(api, new() { ["path"] = "owned" }, verifier);

        await service.HideAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        HidHideVisibilityResult result = await service.HideAndVerifyAsync(
            "controller", ["path"], app, TestContext.Current.CancellationToken);

        Assert.Equal(HidHideVisibilityStatus.Hidden, result.Status);
        Assert.Equal(1, verifier.Calls);
    }
    [Fact]
    public void RejectsInvertedApplicationList() { var api = new FakeApi { IsAppListInverted = true }; Assert.Equal(HidHideAvailability.UnsupportedConfiguration, Create(api, []).GetAvailability()); }
    private HidHideService Create(FakeApi api, Dictionary<string, string> map,
        IHidHideVisibilityVerifier? verifier = null) => new(api, new FakeResolver(map),
        new HidHideJournalStore(Path.Combine(_root, "journal.json")), verifier);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeResolver(Dictionary<string, string> map) : IDeviceInstanceResolver { public string Resolve(string path) => map[path]; }
    private sealed class FakeApi : IHidHideApi
    {
        public bool IsInstalled { get; set; } = true; public bool IsOperational { get; set; } = true; public bool IsActive { get; set; } public bool IsAppListInverted { get; set; }
        public bool ThrowOnRemoveBlockedOnce { get; set; }
        public bool IgnoreBlockedAdds { get; set; }
        public bool ReturnApplicationPathsInDriverNamespace { get; set; }
        public Version LocalDriverVersion { get; set; } = new(1, 5, 230);
        public HashSet<string> Apps { get; } = new(StringComparer.OrdinalIgnoreCase); public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> ApplicationPaths => ReturnApplicationPathsInDriverNamespace
            ? Apps.Select(path => ApplicationPathIdentity.TryToDriverPath(path) ?? path).ToArray()
            : Apps;
        public IReadOnlyCollection<string> BlockedInstanceIds => Blocked;
        public void AddApplicationPath(string path) => Apps.Add(path); public void RemoveApplicationPath(string path) => Apps.Remove(path); public void AddBlockedInstanceId(string id) { if (!IgnoreBlockedAdds) Blocked.Add(id); }
        public void RemoveBlockedInstanceId(string id)
        {
            if (ThrowOnRemoveBlockedOnce) { ThrowOnRemoveBlockedOnce = false; throw new IOException("transient removal failure"); }
            Blocked.Remove(id);
        }
    }
    private sealed class FakeVisibilityVerifier(HidHideVisibilityResult result, string path)
        : IHidHideVisibilityVerifier
    {
        public string ProbeApplicationPath { get; } = Path.GetFullPath(path);
        public int Calls { get; private set; }
        public Task<HidHideVisibilityResult> VerifyHiddenAsync(string stableControllerId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
