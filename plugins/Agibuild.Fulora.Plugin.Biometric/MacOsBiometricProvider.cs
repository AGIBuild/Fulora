using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// macOS biometric provider using LocalAuthentication framework (LAContext)
/// via a native ObjC shim (libAgibuildBiometric.dylib).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacOsBiometricProvider : IBiometricPlatformProvider
{
    /// <inheritdoc />
    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.FromResult(new BiometricAvailability(false, null, "wrong_platform"));

        try
        {
            IntPtr typePtr = IntPtr.Zero;
            IntPtr errorPtr = IntPtr.Zero;
            var available = NativeMethods.ag_biometric_check_availability(out typePtr, out errorPtr) != 0;

            string? biometricType = typePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(typePtr) : null;
            string? errorMsg = null;

            if (errorPtr != IntPtr.Zero)
            {
                errorMsg = Marshal.PtrToStringUTF8(errorPtr);
                NativeMethods.ag_biometric_free(errorPtr);
            }

            return Task.FromResult(new BiometricAvailability(available, biometricType, available ? null : (errorMsg ?? "not_available")));
        }
        catch (DllNotFoundException)
        {
            return Task.FromResult(new BiometricAvailability(false, null, "native_lib_not_found"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BiometricAvailability(false, null, ex.Message));
        }
    }

    /// <inheritdoc />
    public Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.FromResult(new BiometricResult(false, "wrong_platform", "Not running on macOS"));

        var tcs = new TaskCompletionSource<BiometricResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        ct.Register(() => tcs.TrySetResult(new BiometricResult(false, "cancelled", "Operation cancelled")));

        var callbackHandle = GCHandle.Alloc(tcs);

        try
        {
            unsafe
            {
                NativeMethods.ag_biometric_authenticate(
                    reason,
                    GCHandle.ToIntPtr(callbackHandle),
                    &OnAuthReply);
            }
        }
        catch (DllNotFoundException)
        {
            callbackHandle.Free();
            return Task.FromResult(new BiometricResult(false, "native_lib_not_found", "libAgibuildBiometric.dylib not found"));
        }
        catch (Exception ex)
        {
            callbackHandle.Free();
            return Task.FromResult(new BiometricResult(false, "internal_error", ex.Message));
        }

        return tcs.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnAuthReply(IntPtr userData, int success, IntPtr errorMsgPtr)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var tcs = (TaskCompletionSource<BiometricResult>)handle.Target!;
        handle.Free();

        if (success != 0)
        {
            tcs.TrySetResult(new BiometricResult(true, null, null));
        }
        else
        {
            var errorStr = errorMsgPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(errorMsgPtr) : "unknown|Authentication failed";
            var parts = errorStr?.Split('|', 2) ?? ["unknown", "Authentication failed"];
            var errorCode = parts.Length > 0 ? parts[0] : "unknown";
            var errorMessage = parts.Length > 1 ? parts[1] : "Authentication failed";
            tcs.TrySetResult(new BiometricResult(false, errorCode, errorMessage));
        }
    }

    private static partial class NativeMethods
    {
        private const string LibName = "AgibuildBiometric";

        [LibraryImport(LibName)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial int ag_biometric_check_availability(out IntPtr outType, out IntPtr outError);

        [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static unsafe partial void ag_biometric_authenticate(
            string reason,
            IntPtr userData,
            delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void> replyCb);

        [LibraryImport(LibName)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ag_biometric_free(IntPtr ptr);
    }
}
