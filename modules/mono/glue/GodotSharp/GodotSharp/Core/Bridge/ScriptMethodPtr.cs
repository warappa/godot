using Godot.NativeInterop;

namespace Godot.Bridge
{
    public unsafe readonly struct ScriptMethodPtr
    {
        public readonly delegate* managed<GodotObject, in NativeVariantPtrArgs, out godot_variant, void> Ptr;

        public ScriptMethodPtr(delegate* managed<GodotObject, in NativeVariantPtrArgs, out godot_variant, void> ptr)
        {
            Ptr = ptr;
        }

        public static ScriptMethodPtr Create<TV>(delegate* managed<TV, in NativeVariantPtrArgs, out godot_variant, void> ptr)
            where TV : GodotObject
        {
            // Type erasure: TV -> GodotObject
            delegate* managed<GodotObject, in NativeVariantPtrArgs, out godot_variant, void> erasedPtr =
                (delegate* managed<GodotObject, in NativeVariantPtrArgs, out godot_variant, void>)(void*)ptr;

            return new ScriptMethodPtr(erasedPtr);
        }
    }
}
