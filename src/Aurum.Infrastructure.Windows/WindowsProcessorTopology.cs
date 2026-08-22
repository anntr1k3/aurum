using System.Runtime.InteropServices;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsProcessorTopology : IProcessorTopology
{
    private const int RelationProcessorCore = 0;
    private const int ErrorInsufficientBuffer = 122;

    public ProcessorTopologyInfo Capture()
    {
        try
        {
            uint length = 0;
            if (GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length) ||
                Marshal.GetLastWin32Error() != ErrorInsufficientBuffer ||
                length == 0)
            {
                return Fallback();
            }

            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                {
                    return Fallback();
                }

                var coreCount = 0;
                var efficiency = new HashSet<byte>();
                var offset = 0;
                while (offset + 8 <= length)
                {
                    var size = Marshal.ReadInt32(buffer, offset + 4);
                    if (size <= 0)
                    {
                        break;
                    }

                    var relationship = Marshal.ReadInt32(buffer, offset);
                    if (relationship == RelationProcessorCore && offset + 10 <= length)
                    {
                        coreCount++;
                        efficiency.Add(Marshal.ReadByte(buffer, offset + 9));
                    }

                    offset += size;
                }

                if (coreCount == 0)
                {
                    return Fallback();
                }

                return new ProcessorTopologyInfo(
                    Environment.ProcessorCount,
                    coreCount,
                    efficiency.Count,
                    efficiency.Count > 1);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return Fallback();
        }
    }

    private static ProcessorTopologyInfo Fallback() =>
        new(Environment.ProcessorCount, Environment.ProcessorCount, 1, false);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);
}
