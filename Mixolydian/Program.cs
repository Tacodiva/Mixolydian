
using System;
using System.Linq;
using System.Reflection;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public static class Program {
    public static void Main(string[] args) {

        // MixoPatcher.Patch("Game/Orignal", "Game.dll", "Game/Mods", "Game/Patched");
        MixoPatcher.Patch("Celeste/Orignal", "Celeste.exe", "Celeste/Mods", "Celeste/Patched");
        
    }
}