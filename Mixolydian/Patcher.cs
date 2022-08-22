using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace Mixolydian;

public class MixolydianPatcher {

    public readonly string MainAssemblyPath;

    private readonly List<string> _Mods;

    public MixolydianPatcher(string mainAssemblyPath) {
        _Mods = new List<string>();
        MainAssemblyPath = mainAssemblyPath;
    }

    public void AddMod(string dllPath) {
        _Mods.Add(dllPath);
    }

    private record AssemblyInfo(string FileName, AssemblyDefinition Assembly) {
        public AssemblyInfo(AssemblyDefinition assembly) : this(assembly.Name.Name + ".dll", assembly) { }
    }

    public void Patch(string outputDirectory) {
        string gameDirectory = Path.GetDirectoryName(MainAssemblyPath) ?? throw new System.SystemException($"Invalid main assembly path '{MainAssemblyPath}'.");
        AssemblyResolver resolver = new();
        resolver.AddSearchDirectory(gameDirectory);

        Console.WriteLine("===== Loading Game =====\n");

        // Load the main game assembly
        List<AssemblyInfo> gameAssemblies = new(); // Assemblies that are in this list are the ones we can mixin to
        AssemblyDefinition mainAssembly = AssemblyDefinition.ReadAssembly(MainAssemblyPath, new ReaderParameters() { AssemblyResolver = resolver });
        gameAssemblies.Add(new AssemblyInfo(MainAssemblyPath, mainAssembly));
        resolver.AddAssembly(mainAssembly);
        Console.WriteLine($"Loaded main assembly `{mainAssembly}`");

        // Try to load the main game's assembly references from it's current directory
        foreach (ModuleDefinition module in mainAssembly.Modules)
            foreach (AssemblyNameReference dependRef in module.AssemblyReferences) {
                string dllPath = Path.Combine(gameDirectory, dependRef.Name + ".dll");
                if (File.Exists(dllPath)) {
                    AssemblyDefinition dependAssembly = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters() { AssemblyResolver = resolver });
                    resolver.AddAssembly(dependAssembly);
                    gameAssemblies.Add(new AssemblyInfo(dllPath, dependAssembly));
                    Console.WriteLine($"Found and loaded main dependency `{dependAssembly}` at `{dllPath}`");
                }
            }

        Console.WriteLine("\n===== Loading Mods =====\n");

        // Load the mods
        List<AssemblyInfo> modAssemblies = new();
        foreach (string mod in _Mods) {
            AssemblyDefinition modAssembly = AssemblyDefinition.ReadAssembly(mod, new ReaderParameters() { AssemblyResolver = resolver });
            modAssemblies.Add(new AssemblyInfo(mod, modAssembly));
            resolver.AddAssembly(modAssembly);
            Console.WriteLine($"Loaded mod assembly `{modAssembly}`");
        }
        // Parse the mods
        List<Mod> mods = new(modAssemblies.Count);
        foreach (AssemblyInfo modAssembly in modAssemblies) {
            mods.Add(new Mod(modAssembly.Assembly, modAssembly.FileName));
        }

        Console.WriteLine("\n===== Running Mixins =====\n");

        foreach (Mod mod in mods) {
            foreach (TypeMixin typeMixin in mod.TypeMixins) {
                foreach (MethodAccessor methodAccessor in typeMixin.MethodAccessors)
                    methodAccessor.CreateDefinition();
                foreach (MethodInject methodInject in typeMixin.MethodInjectors)
                    methodInject.CreateDefinition();
                foreach (FieldAccessor fieldAccessor in typeMixin.FieldAccessors)
                    fieldAccessor.CreateDefinition();
                foreach (FieldInject fieldInject in typeMixin.FieldInjectors)
                    fieldInject.CreateDefinition();
            }
        }

        foreach (Mod mod in mods) {
            foreach (TypeMixin typeMixin in mod.TypeMixins) {
                foreach (MethodInject methodInject in typeMixin.MethodInjectors)
                    methodInject.Inject();
                typeMixin.InjectConstructor();
            }
        }

        // Sort all the function mixins by their priority
        Dictionary<string, List<FunctionMixin>> functionMixins = new();
        foreach (Mod mod in mods) {
            foreach (TypeMixin typeMixin in mod.TypeMixins) {
                foreach (FunctionMixin functionMixin in typeMixin.FunctionMixins) {
                    string hash = CILUtils.MethodHash(functionMixin.Target);
                    if (!functionMixins.TryGetValue(hash, out List<FunctionMixin>? mixins)) {
                        mixins = new();
                        functionMixins.Add(hash, mixins);
                    }
                    mixins.Add(functionMixin);
                }
            }
        }
        foreach (List<FunctionMixin> mixins in functionMixins.Values)
            mixins.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Inject all the function mixins
        foreach (List<FunctionMixin> mixins in functionMixins.Values) {
            foreach (FunctionMixin mixin in mixins) {
                mixin.Inject();
            }
        }

        Console.WriteLine("\n===== Writing Output =====\n");
        if (Directory.Exists(outputDirectory)) {
            foreach (string file in Directory.EnumerateFiles(outputDirectory))
                File.Delete(file);
            foreach (string dir in Directory.EnumerateDirectories(outputDirectory))
                Directory.Delete(dir, true);
        } else {
            Directory.CreateDirectory(outputDirectory);
        }

        int copyCount = 0;
        foreach (AssemblyInfo assemblyInfo in gameAssemblies) {
            assemblyInfo.Assembly.Write(Path.Combine(outputDirectory, Path.GetFileName(assemblyInfo.FileName)));
            Console.WriteLine($"Wrote assembly {assemblyInfo.Assembly}");
        }

        foreach (string file in Directory.EnumerateFiles(gameDirectory)) {
            string outputFile = Path.Combine(outputDirectory, Path.GetFileName(file));
            if (!File.Exists(outputFile)) {
                File.Copy(file, outputFile);
                ++copyCount;
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(gameDirectory))
            copyCount += CopyDirectory(directory, Path.Combine(outputDirectory, Path.GetFileName(directory)));

        Console.WriteLine("Done.");
    }

    private static int CopyDirectory(string from, string to) {
        int count = 0;
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from)) {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            ++count;
        }
        foreach (string directory in Directory.EnumerateDirectories(from)) {
            count += CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
        return count;
    }


    private class AssemblyResolver : BaseAssemblyResolver {
        private readonly Dictionary<string, AssemblyDefinition> _AssemblyCache;

        public AssemblyResolver() {
            _AssemblyCache = new Dictionary<string, AssemblyDefinition>();
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
            if (_AssemblyCache.TryGetValue(name.FullName, out AssemblyDefinition? value))
                return value;
            return _AssemblyCache[name.FullName] = base.Resolve(name, parameters);
        }

        public AssemblyDefinition? GetFromCache(string fullName) {
            _AssemblyCache.TryGetValue(fullName, out AssemblyDefinition? value);
            return value;
        }

        protected override void Dispose(bool disposing) {
            foreach (AssemblyDefinition value in _AssemblyCache.Values)
                value.Dispose();
            _AssemblyCache.Clear();
            base.Dispose(disposing);
        }

        public void AddAssembly(AssemblyDefinition assembly) {
            _AssemblyCache[assembly.FullName] = assembly;
        }
    }

}