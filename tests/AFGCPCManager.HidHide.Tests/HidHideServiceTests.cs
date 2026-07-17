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
    }
    [Fact]
    public async Task UnhideRemovesOnlySpecifiedControllersEntries()
    {
        string app = Path.Combine(_root, "app.exe"); Directory.CreateDirectory(_root); File.WriteAllText(app, "test"); var api = new FakeApi(); var service = Create(api, new() { ["a"] = "id-a", ["b"] = "id-b" });
        await service.HideAsync("a", ["a"], app, TestContext.Current.CancellationToken); await service.HideAsync("b", ["b"], app, TestContext.Current.CancellationToken); await service.UnhideAsync("a", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("id-a", api.Blocked); Assert.Contains("id-b", api.Blocked);
    }
    [Fact]
    public void RejectsInvertedApplicationList() { var api = new FakeApi { IsAppListInverted = true }; Assert.Equal(HidHideAvailability.UnsupportedConfiguration, Create(api, []).GetAvailability()); }
    private HidHideService Create(FakeApi api, Dictionary<string, string> map) => new(api, new FakeResolver(map), new HidHideJournalStore(Path.Combine(_root, "journal.json")));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeResolver(Dictionary<string, string> map) : IDeviceInstanceResolver { public string Resolve(string path) => map[path]; }
    private sealed class FakeApi : IHidHideApi
    {
        public bool IsInstalled { get; set; } = true; public bool IsOperational { get; set; } = true; public bool IsActive { get; set; } public bool IsAppListInverted { get; set; }
        public Version LocalDriverVersion { get; set; } = new(1, 5, 230);
        public HashSet<string> Apps { get; } = new(StringComparer.OrdinalIgnoreCase); public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> ApplicationPaths => Apps; public IReadOnlyCollection<string> BlockedInstanceIds => Blocked;
        public void AddApplicationPath(string path) => Apps.Add(path); public void RemoveApplicationPath(string path) => Apps.Remove(path); public void AddBlockedInstanceId(string id) => Blocked.Add(id); public void RemoveBlockedInstanceId(string id) => Blocked.Remove(id);
    }
}
