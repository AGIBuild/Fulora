using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation;
using WebKit;
using Selector = ObjCRuntime.Selector;

namespace Spike0bIos;

internal static class CookieBlockProbe
{
	private const string Libobjc = "/usr/lib/libobjc.dylib";

	private static int s_status;

	internal static int Status => Volatile.Read (ref s_status);

	internal static void Run ()
	{
		var deadline = DateTime.UtcNow.AddSeconds (30);
		Volatile.Write (ref s_status, 0);

		try {
			var store = WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;
			var sel = new Selector ("getAllCookies:").Handle;

			IntPtr blockPtr;
			unsafe {
				delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> inv = &CookieCompletionTrampoline;
				blockPtr = Spike0b.Interop.BlockLiteral.GetBlockForFunctionPointer ((IntPtr) inv, IntPtr.Zero);
			}

			if (blockPtr == IntPtr.Zero)
				throw new InvalidOperationException ("_Block_copy returned null");

			try {
				objc_msgSend_void (store.Handle, sel, blockPtr);
			} finally {
				_Block_release (blockPtr);
			}

			while (Volatile.Read (ref s_status) == 0 && DateTime.UtcNow < deadline)
				NSRunLoop.Main.RunUntil (NSDate.Now.AddSeconds (0.1));

			Console.WriteLine ($"[Spike0b] STATUS={Volatile.Read (ref s_status)} (1=ok)");
		} catch (Exception ex) {
			Volatile.Write (ref s_status, -1);
			Console.WriteLine ($"[Spike0b] EXCEPTION: {ex}");
		}
	}

	[UnmanagedCallersOnly (CallConvs = new[] { typeof (CallConvCdecl) })]
	private static void CookieCompletionTrampoline (IntPtr block, IntPtr nsArray)
	{
		Volatile.Write (ref s_status, nsArray != IntPtr.Zero ? 1 : 2);
	}

	[DllImport (Libobjc, EntryPoint = "objc_msgSend")]
	private static extern void objc_msgSend_void (IntPtr self, IntPtr sel, IntPtr arg1);

	[DllImport (Libobjc)]
	private static extern void _Block_release (IntPtr block);
}
