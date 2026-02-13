using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Godot.Bridge
{
    public static class MethodCache<T> where T : class
    {
        private static IntPtr[] _keys;
        private static int[] _argCounts;
        private static ScriptMethodPtr[] _methods;
        private static int _mask;

        public static void Initialize(int capacity, IEnumerable<(IntPtr ptr, int argc, ScriptMethodPtr method)> methods)
        {
            // calculate optimal size (2^n) for optimal masking
            int size = 1;
            while (size < capacity * 2) size <<= 1;

            _mask = size - 1;
            _keys = new IntPtr[size];
            _argCounts = new int[size];
            _methods = new ScriptMethodPtr[size];

            foreach (var m in methods)
            {
                int slot = (int)((long)m.ptr >> 3) & _mask;
                while (_keys[slot] != IntPtr.Zero) slot = (slot + 1) & _mask;

                _keys[slot] = m.ptr;
                _argCounts[slot] = m.argc;
                _methods[slot] = m.method;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static bool TryGet(in IntPtr namePtr, in int argCount, out ScriptMethodPtr method)
        {
            int slot = (int)((long)namePtr >> 3) & _mask;

            for (int i = 0; i < 4; i++)
            {
                int curr = (slot + i) & _mask;
                IntPtr key = _keys[curr];

                if (key == namePtr &&
                    _argCounts[curr] == argCount)
                {
                    method = _methods[curr];
                    return true;
                }

                if (key == IntPtr.Zero)
                {
                    break;
                }
            }

            method = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static ref readonly ScriptMethodPtr TryGetFast(in IntPtr namePtr, int argCount)
        {
            int slot = (int)((long)namePtr >> 3) & _mask;

            for (int i = 0; i < 4; i++)
            {
                int curr = (slot + i) & _mask;
                IntPtr key = _keys[curr];

                if (key == namePtr &&
                    _argCounts[curr] == argCount)
                {
                    return ref _methods[curr];
                }

                if (key == IntPtr.Zero)
                {
                    break;
                }
            }

            return ref Unsafe.NullRef<ScriptMethodPtr>();
        }
    }
}
