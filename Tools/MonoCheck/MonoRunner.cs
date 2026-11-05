// Runs MedievalZen's pure EditMode tests (and the finite-world golden hashes) under Unity's own Mono, outside the editor.
// Built (with the editor's own csc) and launched by run.ps1 next to this file (Tools\MonoCheck). No NUnit reference: attributes are matched by name.
//   mono MonoRunner.exe tests  <dir with MedievalZen.dll + MedievalZen.Tests.dll> [filter] [--all]
//   mono MonoRunner.exe golden <dir>
// Tests that reach native Unity code (ECall, SecurityException "...can only be called from the main thread", etc.) are
// reported as NATIVE, not FAIL. Category("Slow") tests run too.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

public static class MonoRunner
{
    static string[] probe;

    public static int Main(string[] args)
    {
        string mode = args[0];
        string dir = Path.GetFullPath(args[1]);
        string unity = Environment.GetEnvironmentVariable("UNITY_EDITOR") ?? @"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor";
        string project = Environment.GetEnvironmentVariable("MZ_PROJECT") ?? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));   // Tools\MonoCheck is two levels under the project
        string nunit = Directory.GetDirectories(Path.Combine(project, @"Library\PackageCache"), "com.unity.ext.nunit@*")[0] + @"\net40\unity-custom";
        probe = new[] { dir, Path.Combine(unity, @"Data\Managed\UnityEngine"), Path.Combine(unity, @"Data\Managed"), nunit };
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string d in probe) { string p = Path.Combine(d, name); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        };
        Console.WriteLine("runtime: " + (Type.GetType("Mono.Runtime") != null ? "Mono " + Type.GetType("Mono.Runtime").GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null) : "CoreCLR/.NET " + Environment.Version) + "; assemblies: " + dir);
        Assembly tests = Assembly.LoadFrom(Path.Combine(dir, "MedievalZen.Tests.dll"));
        if (mode == "golden") return Golden(tests);
        if (mode == "info")
        {
            foreach (string n in new[] { "MedievalZen.dll", "MedievalZen.Tests.dll" })
                foreach (CustomAttributeData d in Assembly.LoadFrom(Path.Combine(dir, n)).GetCustomAttributesData().Where(d => d.AttributeType.Name == "DebuggableAttribute"))
                    Console.WriteLine(n + ": " + string.Join(",", d.ConstructorArguments.Select(x => x.Value)));
            return 0;
        }
        return Tests(tests, args.Length > 2 && !args[2].StartsWith("--") ? args[2] : null);
    }

    static int Golden(Assembly tests)
    {
        Assembly game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MedievalZen") ?? Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(tests.Location), "MedievalZen.dll"));
        Type gen = game.GetType("MedievalZen.World.WorldGenerator"), prm = game.GetType("MedievalZen.World.WorldGenParams"), keys = game.GetType("MedievalZen.World.PaletteKeys");
        Type hash = tests.GetType("MedievalZen.Tests.FiniteWorldHash");
        string[] parts = (string[])hash.GetField("Parts", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (int seed in (Environment.GetEnvironmentVariable("MZ_SEEDS") ?? "12345,42,777").Split(',').Select(int.Parse))
        {
            object p = Activator.CreateInstance(prm);
            prm.GetField("seed").SetValue(p, seed);
            object pk = keys.GetField("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? keys.GetProperty("Default", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var sw = Stopwatch.StartNew();
            object bp = gen.GetMethods().First(mm => mm.Name == "Generate" && mm.GetParameters()[0].ParameterType == prm).Invoke(null, new[] { p, pk, null });
            ulong[] h = (ulong[])hash.GetMethod("Compute").Invoke(null, new[] { bp });
            Console.Write("seed " + seed + " (" + sw.ElapsedMilliseconds + " ms):");
            for (int i = 0; i < h.Length; i++) Console.Write(" " + parts[i] + "=0x" + h[i].ToString("X16"));
            Console.WriteLine();
        }
        return 0;
    }

    static bool Has(MemberInfo m, string attr) => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attr);
    static bool Has(ParameterInfo m, string attr) => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attr);

    static IEnumerable<object[]> Cases(MethodInfo m)
    {
        var tc = m.GetCustomAttributes(true).Where(a => a.GetType().Name == "TestCaseAttribute").ToList();
        if (tc.Count > 0)
        {
            foreach (object a in tc) yield return (object[])a.GetType().GetProperty("Arguments").GetValue(a);
            yield break;
        }
        ParameterInfo[] ps = m.GetParameters();
        if (ps.Length == 0) { yield return new object[0]; yield break; }
        var lists = new List<object[]>();
        foreach (ParameterInfo p in ps)
        {
            object v = p.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().Name == "ValuesAttribute");
            if (v == null) yield break;
            object[] data = (object[])v.GetType().GetField("data", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(v);
            if (data == null || data.Length == 0)
            {
                Type pt = p.ParameterType;
                data = pt == typeof(bool) ? new object[] { false, true } : pt.IsEnum ? Enum.GetValues(pt).Cast<object>().ToArray() : new object[0];
            }
            lists.Add(data.Select(d => d != null && d.GetType() != p.ParameterType && d is IConvertible && !p.ParameterType.IsEnum ? Convert.ChangeType(d, p.ParameterType) : d).ToArray());
        }
        IEnumerable<object[]> acc = new[] { new object[0] };
        foreach (object[] l in lists) { object[] cur = l; acc = acc.SelectMany(prefix => cur.Select(x => prefix.Concat(new[] { x }).ToArray())).ToList(); }
        foreach (object[] c in acc) yield return c;
    }

    static bool IsNative(Exception e)
    {
        for (; e != null; e = e.InnerException)
        {
            if (e is System.Security.SecurityException || e is MissingMethodException || e is TypeInitializationException && e.InnerException == null) return true;
            string s = e.Message + e.StackTrace;
            if (s.Contains("ECall") || s.Contains("Injected") || s.Contains("can only be called from the main thread") || s.Contains("UnityEditor.") || s.Contains("Internal_")) return true;
        }
        return false;
    }

    static int Tests(Assembly tests, string filter)
    {
        int pass = 0, fail = 0, native = 0;
        Type[] types;
        try { types = tests.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        foreach (Type t in types.Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "MedievalZen.Tests").OrderBy(t => t.Name))
        {
            MethodInfo[] methods;
            try { methods = t.GetMethods(); } catch { continue; }
            MethodInfo setup = methods.FirstOrDefault(mm => Has(mm, "SetUpAttribute")), teardown = methods.FirstOrDefault(mm => Has(mm, "TearDownAttribute"));
            MethodInfo once = methods.FirstOrDefault(mm => Has(mm, "OneTimeSetUpAttribute")), onceDown = methods.FirstOrDefault(mm => Has(mm, "OneTimeTearDownAttribute"));
            object fixture = null;
            foreach (MethodInfo m in methods.OrderBy(mm => mm.Name))
            {
                if (!Has(m, "TestAttribute") && !Has(m, "TestCaseAttribute")) continue;
                if (Has(m, "UnityTestAttribute") || Has(m, "IgnoreAttribute") || Has(m, "ExplicitAttribute")) continue;
                if (filter != null && !(t.Name + "." + m.Name).Contains(filter)) continue;
                foreach (object[] a in Cases(m))
                {
                    string name = t.Name + "." + m.Name + "(" + string.Join(",", a.Select(x => x?.ToString())) + ")";
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        if (fixture == null) { fixture = Activator.CreateInstance(t); once?.Invoke(fixture, null); }
                        object inst = once != null ? fixture : Activator.CreateInstance(t);
                        try { setup?.Invoke(inst, null); object r = m.Invoke(inst, a); if (r is IEnumerator en) while (en.MoveNext()) { } }
                        finally { try { teardown?.Invoke(inst, null); } catch { } }
                        pass++;
                        if (Environment.GetEnvironmentVariable("MZ_VERBOSE") == "1") Console.WriteLine("PASS " + name + " " + sw.ElapsedMilliseconds + " ms");
                    }
                    catch (Exception e)
                    {
                        Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                        if (IsNative(inner)) { native++; if (Environment.GetEnvironmentVariable("MZ_VERBOSE") == "1") Console.WriteLine("NATIVE " + name + ": " + inner.GetType().Name); continue; }
                        if (inner.GetType().Name == "IgnoreException" || inner.GetType().Name == "InconclusiveException" || inner.GetType().Name == "SuccessException") { pass++; continue; }
                        fail++;
                        string msg = inner.Message.Replace("\r", "");
                        string[] lines = msg.Split('\n');
                        Console.WriteLine("FAIL " + name + ": " + inner.GetType().Name + ": " + string.Join(" | ", lines.Take(4)).Trim());
                    }
                }
            }
            if (fixture != null) try { onceDown?.Invoke(fixture, null); } catch { }
        }
        Console.WriteLine(pass + " passed, " + fail + " failed, " + native + " need the Unity engine (skipped)");
        return fail == 0 ? 0 : 1;
    }
}
