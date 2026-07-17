using System.ComponentModel;
using System.Runtime.InteropServices;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Windows.Consumer;

public sealed class WindowsConsumerActionEmitter : IConsumerActionEmitter
{
    public ValueTask EmitAsync(ConsumerAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ushort key = action switch
        {
            ConsumerAction.Rewind => 0xB1,
            ConsumerAction.PlayPause => 0xB3,
            ConsumerAction.FastForward => 0xB0,
            ConsumerAction.BrowserHome => 0xAC,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        Input[] inputs = [new(InputType.Keyboard, new KeyboardInput(key, 0, 0, 0, 0)), new(InputType.Keyboard, new KeyboardInput(key, 0, KeyUp, 0, 0))];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not emit the Windows consumer action.");
        return ValueTask.CompletedTask;
    }

    private const uint KeyUp = 0x0002;
    private enum InputType : uint { Mouse, Keyboard, Hardware }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct KeyboardInput(ushort VirtualKey, ushort Scan, uint Flags, uint Time, nuint ExtraInfo);
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input
    {
        public InputType Type; public InputUnion Value;
        public Input(InputType type, KeyboardInput keyboard) { Type = type; Value = new() { Keyboard = keyboard }; }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
