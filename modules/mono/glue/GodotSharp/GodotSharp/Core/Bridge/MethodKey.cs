using System;

namespace Godot.Bridge
{
    internal readonly struct MethodKey : IEquatable<MethodKey>
    {
        public readonly nint Name;
        public readonly int Argc;

        public MethodKey(nint name, int argc)
        {
            Name = name;
            Argc = argc;
        }

        public bool Equals(MethodKey other) => Name == other.Name && Argc == other.Argc;

        public override int GetHashCode() => HashCode.Combine(Name, Argc);

        public override bool Equals(object obj) => obj is MethodKey && Equals((MethodKey)obj);
    }
}
