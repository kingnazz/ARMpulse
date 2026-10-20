using System.ComponentModel;
using ARMpulse.Core;
using Microsoft.Win32.SafeHandles;

namespace ARMpulse.Windows;

internal static class ProcessActions
{
    // Hold and act through the same kernel handle. Revalidate creation time to reject reused PIDs.
    private static SafeProcessHandle OpenValidated(ProcessIdentity identity, uint access)
    {
        if (identity.Pid <= 4 || identity.Pid == Environment.ProcessId || identity.StartTicks == 0)
            throw new InvalidOperationException("This process is protected from actions in ARMpulse.");
        var handle = Native.OpenProcess(Native.Query | access, false, identity.Pid);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception();
            if (!Native.GetProcessTimes(handle, out var created, out _, out _, out _)) throw new Win32Exception();
            if (created != identity.StartTicks) throw new InvalidOperationException("The original process has exited. Select it again.");
            if (!Native.IsProcessCritical(handle, out var critical) || critical ||
                !Native.ProcessIdToSessionId(identity.Pid, out var session) || session == 0)
                throw new InvalidOperationException("Critical, system, and unverified processes cannot be changed.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    public static void End(ProcessIdentity identity)
    {
        using var handle = OpenValidated(identity, 0x0001);
        if (!Native.TerminateProcess(handle, 1)) throw new Win32Exception();
    }

    public static void LowerPriority(ProcessIdentity identity)
    {
        using var handle = OpenValidated(identity, 0x0200);
        if (!Native.SetPriorityClass(handle, 0x4000)) throw new Win32Exception(); // BELOW_NORMAL_PRIORITY_CLASS
    }
}
