using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace AFGCPCManager.ViGEm;

internal interface IViGEmClientApi : IDisposable
{
    IXbox360TargetApi CreateXbox360Target();
}

internal interface IXbox360TargetApi : IDisposable
{
    bool TryConnect();
    void Submit(ViGEmReport report);
    void Disconnect();
}

internal sealed class NativeViGEmClient : IViGEmClientApi
{
    private readonly ViGEmClient _client;

    public NativeViGEmClient()
    {
        try { _client = new ViGEmClient(); }
        catch (Exception ex) when (IsClientFailure(ex))
        {
            throw new ViGEmException(
                "ViGEmBus is unavailable. Install or repair ViGEmBus, then try Xbox (XInput) again.", ex);
        }
    }

    public IXbox360TargetApi CreateXbox360Target() =>
        new NativeXbox360Target(_client.CreateXbox360Controller());

    public void Dispose() => _client.Dispose();

    private static bool IsClientFailure(Exception exception) =>
        exception is VigemBusNotFoundException or VigemBusAccessFailedException
            or VigemBusVersionMismatchException or VigemAllocFailedException
            or DllNotFoundException or BadImageFormatException
            or EntryPointNotFoundException;
}

internal sealed class NativeXbox360Target(IXbox360Controller controller)
    : IXbox360TargetApi
{
    private bool _connected;

    public bool TryConnect()
    {
        controller.AutoSubmitReport = false;
        try
        {
            controller.Connect();
            _connected = true;
            return true;
        }
        catch (VigemNoFreeSlotException)
        {
            return false;
        }
        catch (Exception ex)
        {
            throw new ViGEmException("ViGEmBus could not create an Xbox controller.", ex);
        }
    }

    public void Submit(ViGEmReport report)
    {
        try
        {
            controller.SetButtonsFull(report.Buttons);
            controller.SetSliderValue(Xbox360Slider.LeftTrigger, report.LeftTrigger);
            controller.SetSliderValue(Xbox360Slider.RightTrigger, report.RightTrigger);
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, report.LeftThumbX);
            controller.SetAxisValue(Xbox360Axis.LeftThumbY, report.LeftThumbY);
            controller.SetAxisValue(Xbox360Axis.RightThumbX, report.RightThumbX);
            controller.SetAxisValue(Xbox360Axis.RightThumbY, report.RightThumbY);
            controller.SubmitReport();
        }
        catch (Exception ex)
        {
            throw new ViGEmException("ViGEmBus rejected an Xbox controller update.", ex);
        }
    }

    public void Disconnect()
    {
        if (!_connected) return;
        try { controller.Disconnect(); }
        catch (Exception ex)
        {
            throw new ViGEmException("ViGEmBus could not remove an Xbox controller cleanly.", ex);
        }
        finally { _connected = false; }
    }

    public void Dispose()
    {
        try { Disconnect(); }
        finally { (controller as IDisposable)?.Dispose(); }
    }
}
