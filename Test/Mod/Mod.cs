namespace Mod;

public static class MyMod {

    public static int A {
        set {
            throw new NotImplementedException();
        }
    }

    public static void Run() {
        Console.WriteLine("My mod is running!");
    }
}