
namespace Game;

public class GameProgram {

    private int Value;

    public GameProgram() {
        Value = 69420;
    }

    public int Test(string arg) {
        Console.WriteLine(Concat<string, int>(new List<string>() { "Throw away " }, Value));
        return Value;
    }

    public B Concat<A, B>(List<A> a, B b) {
        return b;
    }

    ////////////////////////////////////////////////////////

    public static void Main(string[] args) {
        GameProgram gameProgram = new();
        Console.WriteLine(gameProgram.Test("Hello"));
        StaticTest();
    }

    public static void StaticTest() {
        Console.WriteLine("Hello from static test!");
    }

}