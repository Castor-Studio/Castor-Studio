using System.Reflection;
using System.Runtime.InteropServices;
using LibObs;

namespace CastorApplication.Services.Studio;

internal static class LibObsOutputInterop
{
    private static readonly PropertyInfo OutputHandleProperty = typeof(ObsOutput).GetProperty(
        "Handle",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(typeof(ObsOutput).FullName, "Handle");

    // LibObs 0.1.1 does not yet expose obs_output_set_mixers. Raw ffmpeg outputs
    // require this bit mask to create their audio streams.
    public static void SetAudioMixers(ObsOutput output, nuint mixers)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (OutputHandleProperty.GetValue(output) is not SafeHandle handle)
            throw new InvalidOperationException("Le handle natif de la sortie LibObs est inaccessible.");

        var addedReference = false;
        try
        {
            handle.DangerousAddRef(ref addedReference);
            ObsOutputSetMixers(handle.DangerousGetHandle(), mixers);
        }
        finally
        {
            if (addedReference) handle.DangerousRelease();
            GC.KeepAlive(output);
        }
    }

    [DllImport("obs", EntryPoint = "obs_output_set_mixers", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ObsOutputSetMixers(nint output, nuint mixers);
}
