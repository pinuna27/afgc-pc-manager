namespace AFGCPCManager.ViGEm;

public sealed class ViGEmException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
