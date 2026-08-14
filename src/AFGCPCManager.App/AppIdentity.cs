namespace AFGCPCManager.App;

internal static class AppIdentity
{
    public const string ProductName = "AFGC PC Manager";
    public const string GitHubOwner = "pinuna27";
    public const string GitHubRepository = "afgc-pc-manager";
    public const string InstallJournalFileName = "install-journal.json";

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductName);

    public static string SettingsPath => Path.Combine(LocalDataDirectory, "settings.json");
    public static string HidHideJournalPath => Path.Combine(
        LocalDataDirectory, "hidhide-journal.json");
    public static string RuntimeLogPath => Path.Combine(LocalDataDirectory, "runtime.log");
    public static string InstallJournalPath => Path.Combine(
        AppContext.BaseDirectory, InstallJournalFileName);
}
