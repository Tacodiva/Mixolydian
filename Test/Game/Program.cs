
namespace Game;

public class GameProgram {

    private int Value;

    public GameProgram() {
        Value = 1;
    }

    public GameProgram(int value) {
        Value = value + 10;
    }

    public void Print() {
        Console.WriteLine(GetValue());
    }

    public void Print(string prefix) {
        Console.WriteLine(prefix + GetValue());
    }

    public int GetValue() {
        return ++Value;
    }

    ////////////////////////////////////////////////////////

    public static void Main(string[] args) {
        Test(new GameProgram());
        Test(new GameProgram(59));
    }

    public static void Test(GameProgram program) {
        program.Print();
        program.Print("This is the value! ");
    }
}