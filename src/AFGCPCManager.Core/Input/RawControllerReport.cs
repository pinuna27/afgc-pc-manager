namespace AFGCPCManager.Core.Input;

public readonly record struct RawControllerReport(ReadOnlyMemory<byte> Bytes);
