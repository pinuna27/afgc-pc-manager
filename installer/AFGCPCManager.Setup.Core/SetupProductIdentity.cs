namespace AFGCPCManager.Setup.Core;

public static class SetupProductIdentity
{
    public const string ProductName = "AFGC PC Manager";
    public const string GitHubOwner = "pinuna27";
    public const string GitHubRepository = "afgc-pc-manager";
    public const string InstallJournalFileName = "install-journal.json";

    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    public static string TemporaryDirectory => Path.Combine(
        Path.GetTempPath(), ProductName);
}
