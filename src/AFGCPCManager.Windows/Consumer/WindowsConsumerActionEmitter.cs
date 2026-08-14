using System.ComponentModel;
using System.Runtime.InteropServices;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Windows.Consumer;

public sealed class WindowsConsumerActionEmitter : IConsumerActionEmitter
{
    private readonly Func<ushort, bool> _sendVirtualKey;
    private readonly Func<TimeSpan, CancellationToken, ValueTask<bool>> _seekActiveSession;

    public WindowsConsumerActionEmitter()
        : this(SendVirtualKey, new WindowsMediaSessionSeeker().SeekByAsync)
    {
    }

    internal WindowsConsumerActionEmitter(
        Func<ushort, bool> sendVirtualKey,
        Func<TimeSpan, CancellationToken, ValueTask<bool>> seekActiveSession)
    {
        _sendVirtualKey = sendVirtualKey;
        _seekActiveSession = seekActiveSession;
    }

    public async ValueTask EmitAsync(
        ConsumerAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case ConsumerAction.Rewind:
                await _seekActiveSession(-SeekInterval, cancellationToken);
                break;
            case ConsumerAction.FastForward:
                await _seekActiveSession(SeekInterval, cancellationToken);
                break;
            case ConsumerAction.PlayPause:
                EmitVirtualKey(VkMediaPlayPause);
                break;
            case ConsumerAction.BrowserHome:
                EmitVirtualKey(VkBrowserHome);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private void EmitVirtualKey(ushort key)
    {
        if (!_sendVirtualKey(key))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not emit the Windows consumer action.");
    }

    private static bool SendVirtualKey(ushort key)
    {
        Input[] inputs = [new(InputType.Keyboard, new KeyboardInput(key, 0, 0, 0, 0)), new(InputType.Keyboard, new KeyboardInput(key, 0, KeyUp, 0, 0))];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    internal static readonly TimeSpan SeekInterval = TimeSpan.FromSeconds(10);
    private const ushort VkMediaPlayPause = 0xB3;
    private const ushort VkBrowserHome = 0xAC;
    private const uint KeyUp = 0x0002;
    private enum InputType : uint { Mouse, Keyboard, Hardware }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct KeyboardInput(ushort VirtualKey, ushort Scan, uint Flags, uint Time, nuint ExtraInfo);
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public InputType Type; public InputUnion Value;
        public Input(InputType type, KeyboardInput keyboard) { Type = type; Value = new() { Keyboard = keyboard }; }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
