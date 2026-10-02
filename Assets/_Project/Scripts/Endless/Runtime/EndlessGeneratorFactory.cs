using System;
using System.Reflection;
using MedievalZen.World;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Creates the endless world's generator: the real <c>MedievalZen.Endless.Generation.EndlessGenerator</c> when it exists
    /// (looked up by name, constructed with <c>(EndlessParams, PaletteKeys)</c>), else — or when asked to — the
    /// <see cref="StubEndlessGenerator"/>. Looking the type up by name lets the runtime compile and run before the generator
    /// is written; once it exists nothing here needs to change.
    /// </summary>
    public static class EndlessGeneratorFactory
    {
        /// <summary>Full name of the real generator type.</summary>
        public const string GeneratorTypeName = "MedievalZen.Endless.Generation.EndlessGenerator";

        /// <summary>
        /// The generator for <paramref name="parameters"/>. <paramref name="description"/> names what was created (for the
        /// log and the overlay); <paramref name="isStub"/> tells whether it is the stand-in.
        /// </summary>
        public static IEndlessGenerator Create(EndlessParams parameters, PaletteKeys keys, bool forceStub, out string description, out bool isStub)
        {
            if (!forceStub)
            {
                Type type = FindType();
                if (type != null && typeof(IEndlessGenerator).IsAssignableFrom(type))
                {
                    ConstructorInfo constructor = type.GetConstructor(new[] { typeof(EndlessParams), typeof(PaletteKeys) });
                    if (constructor != null)
                    {
                        try
                        {
                            var generator = (IEndlessGenerator)constructor.Invoke(new object[] { parameters, keys });
                            description = type.Name;
                            isStub = false;
                            return generator;
                        }
                        catch (TargetInvocationException e)
                        {
                            UnityEngine.Debug.LogError($"Endless road: {GeneratorTypeName} failed to start ({e.InnerException?.Message ?? e.Message}); using the stub generator instead.");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Endless road: {GeneratorTypeName} has no (EndlessParams, PaletteKeys) constructor; using the stub generator.");
                    }
                }
            }

            description = forceStub ? "StubEndlessGenerator (forced)" : "StubEndlessGenerator (no real generator yet)";
            isStub = true;
            return new StubEndlessGenerator(parameters, keys);
        }

        /// <summary>True when the real generator type is present in the loaded assemblies.</summary>
        public static bool RealGeneratorExists => FindType() != null;

        private static Type FindType()
        {
            Type type = typeof(EndlessGeneratorFactory).Assembly.GetType(GeneratorTypeName, false);
            if (type != null) return type;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(GeneratorTypeName, false);
                if (type != null) return type;
            }

            return null;
        }
    }
}
