using Godot.NativeInterop;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Godot.Bridge
{
    //public delegate void ScriptMethod<in T>(T scriptInstance, NativeVariantPtrArgs args, out godot_variant ret)
    //    where T : GodotObject;

    public unsafe readonly struct ScriptMethodPtr
    {
        public readonly delegate* managed<GodotObject, NativeVariantPtrArgs, out godot_variant, void> Ptr;

        public ScriptMethodPtr(delegate* managed<GodotObject, NativeVariantPtrArgs, out godot_variant, void> ptr)
        {
            Ptr = ptr;
        }

        //public void Invoke(GodotObject instance, NativeVariantPtrArgs args, out godot_variant ret)
        //{
        //    Ptr(instance, args, out ret);
        //}

        public static ScriptMethodPtr Create<TV>(delegate* managed<TV, NativeVariantPtrArgs, out godot_variant, void> ptr)
            where TV : GodotObject
        {
            // Type erasure: TV -> GodotObject
            delegate* managed<GodotObject, NativeVariantPtrArgs, out godot_variant, void> erasedPtr =
                (delegate* managed<GodotObject, NativeVariantPtrArgs, out godot_variant, void>)(void*)ptr;

            return new ScriptMethodPtr(erasedPtr);
        }
    }

    public unsafe sealed class ScriptMethodRegistry<T>
        where T : GodotObject
    {
        internal Dictionary<MethodKey, ScriptMethodPtr> MethodsByNameAndArgc = new();

        internal Dictionary<MethodKey, IntPtr> Aliases { get; } = new();

        private readonly HashSet<IntPtr> _knownMethodNames = new();
        //public new static readonly ScriptMethodRegistry<T> MethodRegistry = new ScriptMethodRegistry<T>()
        //    .Register(new StringName(), 0, BuildInfoInvoker.CreateScriptMethod_GetHashCode())
        //    //new Func<ScriptMethodPtr>(
        //    //    () =>
        //    //    {
        //    //        var action = (T scriptInstance, NativeVariantPtrArgs args, out godot_variant ret) =>
        //    //        {
        //    //            var callRet = scriptInstance.GetHashCode();
        //    //            ret = global::Godot.NativeInterop.VariantUtils.CreateFrom<int>(callRet);
        //    //        };
        //    //        return ScriptMethodPtr.Create<T>(&action);
        //    //    }
        //    //)())
        //    .Compile();
        public ScriptMethodRegistry<T> AddAlias(StringName methodName, int argumentCount, StringName alias) =>
            AddAlias(methodName.NativeValue._data, argumentCount, alias.NativeValue._data);

        public ScriptMethodRegistry<T> Register(StringName methodName, int argumentCount, ScriptMethodPtr method) =>
            Register(methodName.NativeValue._data, argumentCount, method);

        internal ScriptMethodRegistry<T> AddAlias(IntPtr methodName, int argumentCount, IntPtr alias)
        {
            Aliases[new MethodKey(methodName, argumentCount)] = alias;
            return this;
        }

        internal ScriptMethodRegistry<T> Register(IntPtr methodName, int argumentCount, ScriptMethodPtr method)
        {
            MethodsByNameAndArgc[new MethodKey(methodName, argumentCount)] = method;
            _knownMethodNames.Add(methodName);
            return this;
        }

        public ScriptMethodRegistry<T> Compile()
        {
            foreach (var (source, alias) in Aliases)
            {
                if (MethodsByNameAndArgc.TryGetValue(source, out var scriptMethod))
                {
                    // don't apply aliases when we have an actual method for the alias already
                    if (!MethodsByNameAndArgc.ContainsKey(new MethodKey(alias, source.Argc)))
                    {
                        Register(alias, source.Argc, scriptMethod);
                    }
                }
            }

            GD.Print($"Script method registry compiled for {typeof(T)}: size={MethodsByNameAndArgc.Count}, alias_size={Aliases.Count}");
            // TODO: I would like to discard _aliases now to free up memory, but the hierarchy above it still needs it
            //       There are probably lots of aliases, we could at least not copy them and recursively walk our parent
            //       hierarchy as it's only done once (here). Ideas are appreciated
            return this;
        }

        public bool ContainsMethod(in godot_string_name name) => _knownMethodNames.Contains(name._data);

        public bool TryGetMethod(in godot_string_name name, int argumentCount, out ScriptMethodPtr method)
        {
            var key = new MethodKey(name._data, argumentCount);
            return MethodsByNameAndArgc.TryGetValue(key, out method);
        }
    }

    public static class ScriptMethodRegistryExtensions
    {
        // This is an extension method because C# does not allow additional type constraints for an already existing T
        public static ScriptMethodRegistry<T> Register<T, TV>(this ScriptMethodRegistry<T> registry, ScriptMethodRegistry<TV> baseTypeRegistry)
            where T : TV
            where TV : GodotObject
        {
            foreach (var (MethodKey, alias) in baseTypeRegistry.Aliases)
            {
                registry.AddAlias(MethodKey.Name, MethodKey.Argc, alias);
            }

            foreach (var (MethodKey, value) in baseTypeRegistry.MethodsByNameAndArgc)
            {
                registry.Register(MethodKey.Name, MethodKey.Argc, value);
            }

            return registry;
        }
    }
}
