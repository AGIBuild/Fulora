// Minimal objc helpers for Spike 0c (0b block helpers + dynamic class registration).
// Paths follow Microsoft.iOS (dotnet/macios) Constants:
// https://github.com/dotnet/macios/blob/main/src/ObjCRuntime/Constants.cs
// SPDX-License-Identifier: MIT (throwaway spike)

using System.Runtime.InteropServices;

namespace Spike0c.Interop;

internal static unsafe partial class Libobjc
{
	internal const string LibObjcPath = "/usr/lib/libobjc.dylib";
	internal const string LibSystemPath = "/usr/lib/libSystem.dylib";

	internal static IntPtr LinkLibSystem() => dlopen(LibSystemPath, 0);

	[LibraryImport(LibObjcPath)]
	internal static partial IntPtr _Block_copy(BlockLiteral* block);

	[LibraryImport(LibSystemPath, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial IntPtr dlsym(IntPtr handle, string symbol);

	[LibraryImport(LibSystemPath, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial IntPtr dlopen(string path, int mode);

	// --- Dynamic class registration (Spike 0c) ---

	[LibraryImport(LibObjcPath, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial IntPtr objc_lookUpClass(string name);

	[LibraryImport(LibObjcPath, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

	[LibraryImport(LibObjcPath)]
	internal static partial void objc_registerClassPair(IntPtr cls);

	[LibraryImport(LibObjcPath, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial IntPtr sel_registerName(string name);

	[LibraryImport(LibObjcPath, StringMarshalling = StringMarshalling.Utf8)]
	internal static partial byte class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

	[LibraryImport(LibObjcPath)]
	internal static partial IntPtr class_createInstance(IntPtr cls, nuint extraBytes);

	[LibraryImport(LibObjcPath)]
	internal static partial IntPtr objc_retain(IntPtr obj);
}
