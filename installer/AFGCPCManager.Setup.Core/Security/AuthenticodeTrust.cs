using System.Runtime.InteropServices;

namespace AFGCPCManager.Setup.Core.Security;

internal static class AuthenticodeTrust
{
    public static bool IsTrusted(string path)
    {
        var file = new WinTrustFileInfo(path);
        var data = new WinTrustData(file);
        Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            return WinVerifyTrust(0, ref action, ref data) == 0;
        }
        finally
        {
            data.Dispose();
            file.Dispose();
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref WinTrustData data);

    private enum DataChoice : uint { File = 1 }
    private enum UiChoice : uint { None = 2 }
    private enum RevocationChecks : uint { None = 0 }
    private enum StateAction : uint { Ignore = 0 }
    [Flags]
    private enum ProviderFlags : uint { RevocationCheckChainExcludeRoot = 0x80 }
    private enum UiContext : uint { Execute = 0 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo : IDisposable
    {
        public uint Size;
        public nint FilePath;
        public nint FileHandle;
        public nint KnownSubject;

        public WinTrustFileInfo(string path)
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = Marshal.StringToCoTaskMemUni(path);
            FileHandle = 0;
            KnownSubject = 0;
        }

        public void Dispose()
        {
            if (FilePath != 0)
                Marshal.FreeCoTaskMem(FilePath);
            FilePath = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData : IDisposable
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public UiChoice UiChoice;
        public RevocationChecks RevocationChecks;
        public DataChoice UnionChoice;
        public nint FileInfo;
        public StateAction StateAction;
        public nint StateData;
        public string? UrlReference;
        public ProviderFlags ProviderFlags;
        public UiContext UiContext;

        public WinTrustData(WinTrustFileInfo file)
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = 0;
            SipClientData = 0;
            UiChoice = UiChoice.None;
            RevocationChecks = RevocationChecks.None;
            UnionChoice = DataChoice.File;
            FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, FileInfo, false);
            StateAction = StateAction.Ignore;
            StateData = 0;
            UrlReference = null;
            ProviderFlags = ProviderFlags.RevocationCheckChainExcludeRoot;
            UiContext = UiContext.Execute;
        }

        public void Dispose()
        {
            if (FileInfo != 0)
                Marshal.FreeCoTaskMem(FileInfo);
            FileInfo = 0;
        }
    }
}
