// SPDX-License-Identifier: MIT
//
// ATTRIBUTION — vendored for Fulora Phase 0 Spike 0b (throwaway).
// Upstream: https://github.com/AvaloniaUI/Avalonia.Controls.WebView/blob/4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e/src/Avalonia.Controls.WebView.Core/Macios/Interop/BlockLiteral.cs
// Commit SHA: 4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e (file content matches main branch as of 2026-04-25)
//
// Modifications vs upstream:
// - Namespace changed to Spike0b.Interop.
// - Libobjc paths: use /usr/lib/libSystem.dylib for dlopen/dlsym (iOS-compatible; Avalonia used libdl on macOS).
//
// ---
// MIT License (upstream Avalonia.Controls.WebView)
//
// Copyright (c) 2026 AvaloniaUI OÜ
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Runtime.InteropServices;

namespace Spike0b.Interop;

[StructLayout(LayoutKind.Sequential)]
internal unsafe ref struct BlockDescriptor
{
	public static IntPtr GlobalDescriptor { get; }

	static BlockDescriptor()
	{
		GlobalDescriptor = Marshal.AllocHGlobal(sizeof(BlockDescriptor));
		var descriptor = (BlockDescriptor*)(void*)GlobalDescriptor;
		descriptor->size = sizeof(BlockLiteral);
	}

	private long reserved; // always nil
	private long size; // size of the entire Block_literal
	private IntPtr copy_helper;
	private IntPtr dispose_helper;
}

[StructLayout(LayoutKind.Sequential)]
internal ref struct BlockLiteral(IntPtr invoke)
{
	private static IntPtr stackBlock_class;
	private static IntPtr globalBlock_class;

	private static IntPtr NSConcreteStackBlock
	{
		get
		{
			if (stackBlock_class == IntPtr.Zero)
				stackBlock_class = Libobjc.dlsym(Libobjc.LinkLibSystem(), "_NSConcreteStackBlock");
			return stackBlock_class;
		}
	}

	private static IntPtr NSConcreteGlobalBlock
	{
		get
		{
			if (globalBlock_class == IntPtr.Zero)
				globalBlock_class = Libobjc.dlsym(Libobjc.LinkLibSystem(), "_NSConcreteGlobalBlock");
			return globalBlock_class;
		}
	}

	private IntPtr isa;
	private BlockFlags flags;
	private int reserved;
	private IntPtr invoke = invoke;
	private IntPtr block_descriptor = BlockDescriptor.GlobalDescriptor;
	private IntPtr state;

	public static unsafe IntPtr GetCallback(IntPtr blockPtr)
	{
		var block = (BlockLiteral*)(void*)blockPtr;
		return block->invoke;
	}

	public static unsafe IntPtr TryGetBlockState(IntPtr blockPtr)
	{
		var block = (BlockLiteral*)(void*)blockPtr;
		if (block->block_descriptor == BlockDescriptor.GlobalDescriptor)
			return block->state;

		return default;
	}

	public static unsafe IntPtr GetBlockForFunctionPointer(IntPtr callback, IntPtr state)
	{
		var block = new BlockLiteral(callback);
		block.isa = NSConcreteGlobalBlock;
		block.state = state;

		return Libobjc._Block_copy(&block);
	}

	public static unsafe IntPtr GetStackBlockForFunctionPointer(IntPtr callback, IntPtr state)
	{
		var block = new BlockLiteral(callback);
		block.isa = NSConcreteStackBlock;
		block.state = state;

		return Libobjc._Block_copy(&block);
	}

	[Flags]
	private enum BlockFlags
	{
		BLOCK_REFCOUNT_MASK = 0xffff,
		BLOCK_NEEDS_FREE = 1 << 24,
		BLOCK_HAS_COPY_DISPOSE = 1 << 25,
		BLOCK_HAS_CTOR = 1 << 26,
		BLOCK_IS_GC = 1 << 27,
		BLOCK_IS_GLOBAL = 1 << 28,
		BLOCK_HAS_DESCRIPTOR = 1 << 29,
		BLOCK_HAS_STRET = 1 << 29,
		BLOCK_HAS_SIGNATURE = 1 << 30,
	}
}
