// Minimal objc block helpers for Spike 0b. Paths follow Microsoft.iOS (dotnet/macios) Constants:
// https://github.com/dotnet/macios/blob/main/src/ObjCRuntime/Constants.cs
// SPDX-License-Identifier: MIT (same spike as BlockLiteral.cs — throwaway)

using System.Runtime.InteropServices;

namespace Spike0b.Interop;

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
}
