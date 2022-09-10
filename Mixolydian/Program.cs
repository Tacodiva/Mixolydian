using System;
using System.Linq;
using System.Reflection;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public static class Program {
    public static void Main(string[] args) {

        try {

            MixolydianPatcher patcher = new("Game/Orignal/Game.dll");
            patcher.AddMod("Game/Mods/Mod.dll");
            patcher.Patch("Game/Patched");

            // MixolydianPatcher patcher = new("Celeste/Orignal/Celeste.exe");
            // patcher.AddMod("Celeste/Mods/CelesteMod.dll");
            // patcher.Patch("Celeste/Patched");
        } catch (InvalidModException invalidModException) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("================\n\nAn invalid mod was detected!\n");
            Console.Error.WriteLine(" " + invalidModException.Message + "\n");
            Console.Error.WriteLine($" File: {invalidModException.Mod.FileName}");
            Console.Error.WriteLine($" Assembly: {invalidModException.Mod.Assembly}");
            Console.Error.WriteLine($" Mixin: {invalidModException.Mixin?.Source.ToString() ?? "N/A"}");
            Console.Error.WriteLine($" Member: {invalidModException.Member?.ToString() ?? "N/A"}");
#if DEBUG
            Console.Error.WriteLine("\n" + invalidModException);
#endif
            Console.Error.WriteLine("\n================");
            Environment.Exit(1);
        }
    }
}