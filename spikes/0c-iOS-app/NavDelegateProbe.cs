using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using Spike0c.Interop;
using WebKit;
using Selector = ObjCRuntime.Selector;

namespace Spike0cIos;

/// <summary>
/// Spike 0c: WKNavigationDelegate-shaped dynamic class via objc_allocateClassPair + class_addMethod,
/// wired with <see cref="UnmanagedCallersOnly"/> IMP; navigationDelegate set via raw objc_msgSend.
/// </summary>
internal static class NavDelegateProbe
{
	private const string LibobjcDll = "/usr/lib/libobjc.dylib";
	private const string DynamicClassName = "FuloraSpikeNavDelegate0c";

	private static int s_status;

	/// <summary>Holds retained delegate so <c>navigationDelegate</c> (weak) does not drop before callback.</summary>
	private static IntPtr s_retainedDelegate;

	internal static int Status => Volatile.Read (ref s_status);

	internal static void Run (WKWebView webView)
	{
		var deadline = DateTime.UtcNow.AddSeconds (30);
		Volatile.Write (ref s_status, 0);

		try {
			ArgumentNullException.ThrowIfNull (webView);

			var super = Class.GetHandle ("NSObject");
			if (super == IntPtr.Zero)
				throw new InvalidOperationException ("NSObject class handle is null");

			IntPtr cls = Libobjc.objc_lookUpClass (DynamicClassName);
			if (cls == IntPtr.Zero) {
				cls = Libobjc.objc_allocateClassPair (super, DynamicClassName, 0);
				if (cls == IntPtr.Zero)
					throw new InvalidOperationException ("objc_allocateClassPair returned null");

				IntPtr selNav = Libobjc.sel_registerName ("webView:didFinishNavigation:");
				unsafe {
					delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> imp = &DidFinishNavigationTrampoline;
					byte ok = Libobjc.class_addMethod (cls, selNav, (IntPtr)(void*)imp, "v@:@@");
					if (ok == 0)
						throw new InvalidOperationException ("class_addMethod returned false");
				}

				Libobjc.objc_registerClassPair (cls);
			}

			IntPtr del = Libobjc.class_createInstance (cls, 0);
			if (del == IntPtr.Zero)
				throw new InvalidOperationException ("class_createInstance returned null");

			// navigationDelegate is weak; retain for the duration of the probe.
			_ = Libobjc.objc_retain (del);
			s_retainedDelegate = del;

			IntPtr setDel = new Selector ("setNavigationDelegate:").Handle;
			objc_msgSend_id (webView.Handle, setDel, del);

			webView.LoadRequest (new NSUrlRequest (new NSUrl ("about:blank")));

			while (Volatile.Read (ref s_status) == 0 && DateTime.UtcNow < deadline)
				NSRunLoop.Main.RunUntil (NSDate.Now.AddSeconds (0.1));

			Console.WriteLine ($"[Spike0c] STATUS={Volatile.Read (ref s_status)} (1=didFinishNavigation fired)");
		} catch (Exception ex) {
			Volatile.Write (ref s_status, -1);
			Console.WriteLine ($"[Spike0c] EXCEPTION: {ex}");
		}
	}

	[UnmanagedCallersOnly (CallConvs = new[] { typeof (CallConvCdecl) })]
	private static void DidFinishNavigationTrampoline (IntPtr self, IntPtr cmd, IntPtr webView, IntPtr navigation)
	{
		_ = self;
		_ = cmd;
		_ = webView;
		_ = navigation;
		Volatile.Write (ref s_status, 1);
	}

	[DllImport (LibobjcDll, EntryPoint = "objc_msgSend")]
	private static extern void objc_msgSend_id (IntPtr self, IntPtr sel, IntPtr arg1);
}
