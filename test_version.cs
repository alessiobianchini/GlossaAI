using System;
public class Test {
    public static void Main() {
        var v1 = new Version("1.0.8");
        var v2 = new Version("1.0.8.0");
        Console.WriteLine($"latest(1.0.8) > current(1.0.8.0): {v1 > v2}");
        Console.WriteLine($"latest(1.0.8) < current(1.0.8.0): {v1 < v2}");
    }
}
