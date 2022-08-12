using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace Mixolydian;

public static class MixoPatcher {

    public static void Patch(string gameDirectory, string mainAssemblyFile, string modDirectory, string outputDirectory) {
        MixoAssemblyResolver resolver = new(gameDirectory);

        Console.WriteLine("\n===== Loading Mods =====");
        List<MixoMod> mods = new();
        foreach (string modFile in Directory.EnumerateFiles(modDirectory)) {
            AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(modFile, new ReaderParameters { AssemblyResolver = resolver });
            Console.WriteLine($"Loaded mod assembly {mod}");
            mods.Add(new MixoMod(mod));
        }

        Console.WriteLine("\n===== Loading Game =====");
        string mainAssemblyPath = Path.Combine(gameDirectory, mainAssemblyFile);
        AssemblyDefinition mainAssembly = AssemblyDefinition.ReadAssembly(mainAssemblyPath, new ReaderParameters() { AssemblyResolver = resolver });
        Dictionary<string, AssemblyDefinition> fileDefMap = new() {
            { mainAssemblyPath, mainAssembly }
        };
        resolver.AddAssembly(mainAssembly);
        Console.WriteLine($"Loaded master assembly {mainAssemblyFile}");
        foreach (ModuleDefinition module in mainAssembly.Modules)
            foreach (AssemblyNameReference dependRef in module.AssemblyReferences) {
                string dllPath = Path.Combine(gameDirectory, dependRef.Name + ".dll");
                if (File.Exists(dllPath)) {
                    AssemblyDefinition dependAssembly = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters() { AssemblyResolver = resolver });
                    resolver.AddAssembly(dependAssembly);
                    fileDefMap.Add(dllPath, dependAssembly);
                    Console.WriteLine($"Loaded local assembly {dependRef}");
                }
            }

        Console.WriteLine("\n===== Patching =====");
        foreach (MixoMod mod in mods)
            MixoCILPatcher.Apply(mod);

        Console.WriteLine("\n===== Writing Output =====");
        if (Directory.Exists(outputDirectory)) {
            foreach (string file in Directory.EnumerateFiles(outputDirectory))
                File.Delete(file);
            foreach (string dir in Directory.EnumerateDirectories(outputDirectory))
                Directory.Delete(dir, true);
        } else {
            Directory.CreateDirectory(outputDirectory);
        }

        int copyCount = 0;
        foreach (string file in Directory.EnumerateFiles(gameDirectory)) {
            if (fileDefMap.TryGetValue(file, out AssemblyDefinition? def)) {
                // If we've loaded the assembly, write the potentially modified version
                def.Write(Path.Combine(outputDirectory, Path.GetFileName(file)));
                Console.WriteLine($"Wrote assembly {def}");
            } else {
                // Otherwise, just copy the file
                File.Copy(file, Path.Combine(outputDirectory, Path.GetFileName(file)));
                ++copyCount;
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(gameDirectory))
            copyCount += CopyDirectory(directory, Path.Combine(outputDirectory, Path.GetFileName(directory)));
        Console.WriteLine($"Copied {copyCount} files");

        Console.WriteLine("\nDone\n");
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

    private class MixoAssemblyResolver : BaseAssemblyResolver {
        private readonly Dictionary<string, AssemblyDefinition> _AssemblyCache;

        public MixoAssemblyResolver(string gameDirectory) {
            _AssemblyCache = new Dictionary<string, AssemblyDefinition>();
            AddSearchDirectory(gameDirectory);
        }
        
        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
            if (_AssemblyCache.TryGetValue(name.FullName, out AssemblyDefinition? value))
                return value;
            Console.WriteLine($"Resolving assembly {name}");
            return _AssemblyCache[name.FullName] = base.Resolve(name);
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