
using System;
using System.Linq;
using System.Reflection;
using Mixolydian.Common;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mixolydian;

public static class Program {
    public static void Main(string[] args) {

        MixolydianPatcher patcher = new("Game/Orignal/Game.dll");
        patcher.AddMod("Game/Mods/Mod.dll");
        patcher.Patch("Game/Patched");
        
    }
}