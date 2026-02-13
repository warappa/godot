using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Godot
{
    public partial class GodotObject
    {
        //private IntPtr _registryHandle = (IntPtr)(-1);

        // Im Konstruktor registrieren
        internal void InitializeRegistry()
        {
            //GodotObjectRegistry.Register(NativePtr, this);
        }

        //// Das ist der zentrale Einstiegspunkt für das Löschen
        //public void Dispose()
        //{
        //    Dispose(true);
        //    GC.SuppressFinalize(this);
        //}

        protected virtual void DisposeScriptIntegration(bool disposing)
        {
            if (NativePtr != IntPtr.Zero)
            {
                // Wir geben den Slot in der Registry frei
                //GodotObjectRegistry.Unregister(NativePtr);
                //_registryHandle = (IntPtr)(-1);
            }

            if (disposing)
            {
                // Native Ressourcen freigeben
            }
        }

        //~GodotObject()
        //{
        //    // Falls der Nutzer vergessen hat Dispose zu rufen
        //    Dispose(false);
        //}
    }

    //internal static class GodotObjectRegistry
    //{
    //    // Wir nutzen ein flaches Array für maximale Read-Performance (Lock-Free)
    //    private static GodotObject?[] _objects = new GodotObject[16384];

    //    // Die Queue ersetzt den lock-basierten Stack für freie Indizes
    //    private static readonly ConcurrentQueue<int> _freeIndices = new();

    //    private static int _nextIndex = 0;
    //    private static readonly object _allocationLock = new();

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public static GodotObject? Get(IntPtr handle)
    //    {
    //        int index = (int)handle;
    //        // JIT optimiert diesen Check weg, wenn er inlined wird
    //        if ((uint)index < (uint)_objects.Length)
    //        {
    //            return Volatile.Read(ref _objects[index]);
    //        }
    //        return null;
    //    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public static GodotObject GetFast(IntPtr handle)
    //    {
    //        // Absolutes Maximum an Speed: Kein Bounds-Check, kein Lock.
    //        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_objects), (int)handle)!;
    //    }

    //    public static IntPtr Register(GodotObject obj)
    //    {
    //        // Wir versuchen zuerst, einen recycelten Index zu finden (Lock-Free)
    //        if (_freeIndices.TryDequeue(out int recycledIndex))
    //        {
    //            Volatile.Write(ref _objects[recycledIndex], obj);
    //            return (IntPtr)recycledIndex;
    //        }

    //        // Nur wenn wir wachsen müssen, brauchen wir ein Lock (seltener Pfad)
    //        lock (_allocationLock)
    //        {
    //            int index = _nextIndex++;
    //            if (index >= _objects.Length)
    //            {
    //                var newArray = new GodotObject[_objects.Length * 2];
    //                Array.Copy(_objects, newArray, _objects.Length);
    //                // Atomarer Austausch des Arrays für Lock-Free Reads
    //                _objects = newArray;
    //            }
    //            Volatile.Write(ref _objects[index], obj);
    //            return (IntPtr)index;
    //        }
    //    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public static void Unregister(IntPtr handle)
    //    {
    //        int index = (int)handle;
    //        if (index < 0) return;

    //        // 1. Objekt-Referenz atomar entfernen, damit der GC es abholen kann
    //        Volatile.Write(ref _objects[index], null);

    //        // 2. Index zur Wiederverwendung freigeben (Lock-Free)
    //        _freeIndices.Enqueue(index);
    //    }
    //}

    public static class GodotObjectRegistry
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct Entry
        {
            public IntPtr Key;          // Native Pointer (Godot)
            public GodotObject? Value;  // Direkte starke Referenz für Speed
        }

        private sealed class Table
        {
            public readonly Entry[] Entries;
            public readonly int Mask;
            public int Count;

            public Table(int size)
            {
                Entries = new Entry[size];
                Mask = size - 1;
                Count = 0;
            }
        }

        // Trennung von Lese- und Schreibdaten zur Vermeidung von Cache-Line Contention
        [StructLayout(LayoutKind.Explicit, Size = 128)]
        private struct RegistryState
        {
            [FieldOffset(0)] public Table CurrentTable;
            [FieldOffset(64)] public readonly object WriteLock;

            public RegistryState(int initialSize)
            {
                CurrentTable = new Table(initialSize);
                WriteLock = new object();
            }
        }

        private static RegistryState _state = new RegistryState(4096);

        /// <summary>
        /// Holt das Objekt. Zeitkomplexität: Fast immer O(1). 
        /// Keine Locks, keine GCHandles, kein Overhead.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static GodotObject? Get(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero) return null;

            // Atomarer Snapshot der Tabelle für Lock-free Reads
            var table = _state.CurrentTable;
            int mask = table.Mask;
            var entries = table.Entries;

            // Fibonacci Hash für optimale Pointer-Verteilung
            int slot = (int)(((ulong)nativePtr.ToInt64() * 11400714819323198485uL) >> 32) & mask;

            for (int i = 0; i < 32; i++) // Kurzes Linear Probing für L1-Cache Effizienz
            {
                ref readonly var entry = ref entries[(slot + i) & mask];

                if (entry.Key == nativePtr)
                    return entry.Value;

                if (entry.Key == IntPtr.Zero)
                    break;
            }

            return null;
        }

        public static void Register(IntPtr nativePtr, GodotObject obj)
        {
            if (nativePtr == IntPtr.Zero) return;

            lock (_state.WriteLock)
            {
                // Load Factor 0.7 überschritten? -> Resize
                if (_state.CurrentTable.Count >= _state.CurrentTable.Entries.Length * 0.7)
                {
                    Resize();
                }

                var table = _state.CurrentTable;
                int mask = table.Mask;
                int slot = (int)(((ulong)nativePtr.ToInt64() * 11400714819323198485uL) >> 32) & mask;

                while (table.Entries[slot].Key != IntPtr.Zero)
                {
                    if (table.Entries[slot].Key == nativePtr)
                    {
                        table.Entries[slot].Value = obj;
                        return;
                    }
                    slot = (slot + 1) & mask;
                }

                // Erst Value setzen, dann Key (Publishing-Barrier via Volatile)
                table.Entries[slot].Value = obj;
                Volatile.Write(ref table.Entries[slot].Key, nativePtr);
                table.Count++;
            }
        }

        public static void Unregister(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero) return;

            lock (_state.WriteLock)
            {
                var table = _state.CurrentTable;
                int mask = table.Mask;
                int slot = (int)(((ulong)nativePtr.ToInt64() * 11400714819323198485uL) >> 32) & mask;

                int i = slot;
                while (table.Entries[i].Key != IntPtr.Zero)
                {
                    if (table.Entries[i].Key == nativePtr)
                    {
                        RemoveAndRehash(table, i);
                        table.Count--;
                        return;
                    }
                    i = (i + 1) & mask;
                }
            }
        }

        private static void RemoveAndRehash(Table table, int i)
        {
            int mask = table.Mask;
            table.Entries[i].Key = IntPtr.Zero;
            table.Entries[i].Value = null;

            int j = i;
            while (true)
            {
                j = (j + 1) & mask;
                IntPtr k = table.Entries[j].Key;
                if (k == IntPtr.Zero) break;

                // Berechne idealen Slot für das verschobene Element
                int r = (int)(((ulong)k.ToInt64() * 11400714819323198485uL) >> 32) & mask;

                // Liegt r außerhalb der aktuellen Kette? Dann verschieben.
                if ((i <= j) ? (i < r && r <= j) : (i < r || r <= j))
                    continue;

                table.Entries[i] = table.Entries[j];
                table.Entries[j].Key = IntPtr.Zero;
                table.Entries[j].Value = null;
                i = j;
            }
        }

        private static void Resize()
        {
            int newSize = _state.CurrentTable.Entries.Length * 2;
            var newTable = new Table(newSize);
            var oldTable = _state.CurrentTable;

            foreach (var entry in oldTable.Entries)
            {
                if (entry.Key != IntPtr.Zero)
                {
                    int slot = (int)(((ulong)entry.Key.ToInt64() * 11400714819323198485uL) >> 32) & newTable.Mask;
                    while (newTable.Entries[slot].Key != IntPtr.Zero)
                    {
                        slot = (slot + 1) & newTable.Mask;
                    }
                    newTable.Entries[slot] = entry;
                    newTable.Count++;
                }
            }

            // Atomarer Austausch macht die neue Tabelle für Get() sichtbar
            _state.CurrentTable = newTable;
        }
    }





    //public static class GodotObjectRegistry
    //{
    //    private struct Entry
    //    {
    //        public IntPtr Key;         // Der native Pointer von Godot
    //        public GodotObject? Value; // Das managed C# Objekt
    //    }

    //    // Wir nutzen ein flaches Array für maximale Cache-Lokalität
    //    private static Entry[] _entries;
    //    private static int _mask;
    //    private static int _count;
    //    private static readonly object _writeLock = new();

    //    static GodotObjectRegistry()
    //    {
    //        const int initialSize = 4096; // Muss eine Potenz von 2 sein
    //        _entries = new Entry[initialSize];
    //        _mask = initialSize - 1;
    //    }

    //    /// <summary>
    //    /// Holt das Objekt zum nativen Pointer. Absolut Hot-Path optimiert.
    //    /// </summary>
    //    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    //    public static GodotObject? Get(IntPtr gcHandlePtr)
    //    {
    //        //Console.WriteLine($"GodotObjectRegistry.Get: {gcHandlePtr}");
    //        if (gcHandlePtr == IntPtr.Zero) return null;

    //        // Hash für Pointer: Shiften (8-byte alignment) und Maskieren
    //        var entries = _entries;
    //        int mask = _mask;
    //        int slot = (int)((long)gcHandlePtr >> 3) & mask;

    //        // Wir suchen in einer kurzen Kette (Linear Probing). 
    //        // Bei < 50% Last ist das fast immer der erste Treffer.
    //        int i;
    //        for (i = 0; i < 16; i++)
    //        {
    //            ref readonly var entry = ref entries[(slot + i) & mask];

    //            if (entry.Key == gcHandlePtr)
    //            {
    //                //Console.WriteLine($"GodotObjectRegistry.Get: Found {gcHandlePtr}");
    //                return entry.Value;
    //            }

    //            if (entry.Key == IntPtr.Zero)
    //                break;
    //        }

    //        if (i == 16)
    //        {
    //            Console.WriteLine("Max iteration reached!");
    //        }

    //        Console.WriteLine($"GodotObjectRegistry.Get: NOT Found {gcHandlePtr}");

    //        return null;
    //    }

    //    /// <summary>
    //    /// Registriert ein neues Objekt. Sicher gegenüber parallelen Schreibvorgängen.
    //    /// </summary>
    //    public static void Register(IntPtr gcHandlePtr, GodotObject obj)
    //    {
    //        Console.WriteLine($"GodotObjectRegistry.Register: {obj.GetType().FullName} {gcHandlePtr}, obj {obj.NativeInstance}");

    //        if (gcHandlePtr == IntPtr.Zero) return;

    //        lock (_writeLock)
    //        {
    //            // Vergrößern, wenn wir über 50% Kapazität kommen (Load Factor 0.5)
    //            if (_count >= _entries.Length / 2)
    //            {
    //                Resize();
    //            }

    //            int mask = _mask;
    //            int slot = (int)((long)gcHandlePtr >> 3) & mask;

    //            while (_entries[slot].Key != IntPtr.Zero &&
    //                _entries[slot].Key != gcHandlePtr)
    //            {
    //                slot = (slot + 1) & mask;
    //            }

    //            if (_entries[slot].Key == IntPtr.Zero)
    //            {
    //                _entries[slot].Key = gcHandlePtr;
    //                _count++;
    //            }

    //            // Volatile stellt sicher, dass Get() den Wert sofort sieht
    //            Volatile.Write(ref _entries[slot].Value, obj);

    //            Console.WriteLine($"GodotObjectRegistry.Register: Saved in slot {slot}");
    //        }
    //    }

    //    /// <summary>
    //    /// Entfernt die Referenz, damit der GC das Objekt aufräumen kann.
    //    /// </summary>
    //    public static void Unregister(IntPtr nativePtr)
    //    {
    //        Console.WriteLine($"GodotObjectRegistry.Unregister: {nativePtr}");
    //        if (nativePtr == IntPtr.Zero) return;

    //        lock (_writeLock)
    //        {
    //            int mask = _mask;
    //            int slot = (int)((long)nativePtr >> 3) & mask;

    //            while (_entries[slot].Key != IntPtr.Zero)
    //            {
    //                if (_entries[slot].Key == nativePtr)
    //                {
    //                    // Wir setzen NUR den Value auf null (Tombstone-Prinzip).
    //                    // Den Key zu nullen würde die Kette für Linear Probing unterbrechen.
    //                    Volatile.Write(ref _entries[slot].Value, null);
    //                    Console.WriteLine($"GodotObjectRegistry.Unregister: Found {nativePtr}");
    //                    return;
    //                }
    //                slot = (slot + 1) & mask;
    //            }
    //        }

    //        Console.WriteLine($"GodotObjectRegistry.Unregister: NOT Found {nativePtr}");
    //    }

    //    private static void Resize()
    //    {
    //        int newSize = _entries.Length * 2;
    //        var newEntries = new Entry[newSize];
    //        int newMask = newSize - 1;

    //        // Re-Hash aller existierenden Elemente
    //        foreach (var entry in _entries)
    //        {
    //            if (entry.Key != IntPtr.Zero && entry.Value != null)
    //            {
    //                int slot = (int)((long)entry.Key >> 3) & newMask;
    //                while (newEntries[slot].Key != IntPtr.Zero)
    //                {
    //                    slot = (slot + 1) & newMask;
    //                }
    //                newEntries[slot] = entry;
    //            }
    //        }

    //        // Atomarer Austausch für unterbrechungsfreie Get-Aufrufe
    //        _mask = newMask;
    //        _entries = newEntries;
    //    }
    //}

}
