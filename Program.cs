using System.Text;

namespace Umbra.Core;

/// <summary>
/// Command-line entry point for the Umbra compiler and virtual machine.
/// Supports running <c>.umb</c> source files, the built-in acceptance test suite, and an interactive REPL.
/// </summary>
public static class Program
{
    public const string Version = "1.0.0";

    /// <summary>
    /// Built-in acceptance test program specified in the Umbra language specification.
    /// </summary>
    public const string AcceptanceTestScript = """
        // 1. Работа со списками и индексацией
        mut scores = [10, 20, 30];
        scores[1] = 99;
        print scores; // Ожидается: [10, 99, 30]
        print scores[0] + scores[-1]; // 10 + 30 = 40

        // 2. Встроенные нативные функции и пайплайны
        print len(scores); // Ожидается: 3
        print scores |> len; // Ожидается: 3

        push(scores, 500);
        print scores |> len; // Ожидается: 4
        let last = pop(scores);
        print last; // Ожидается: 500

        // 3. Типы и приведение в строки
        print type(scores); // "list"
        print type("Umbra"); // "string"
        print type(123.45); // "number"
        print type(len); // "native"

        // 4. Цикл for .. in
        let fruits = ["Яблоко", "Груша", "Слива"];
        for (f in fruits) {
            print "Фрукт: " + f;
        }

        // 5. Алгоритм: сортировка пузырьком (Bubble Sort) на чистой Umbra
        mut arr = [64, 34, 25, 12, 22, 11, 90];
        mut i = 0;
        let n = len(arr);

        while (i < n) {
            mut j = 0;
            while (j < n - i - 1) {
                if (arr[j] > arr[j + 1]) {
                    let temp = arr[j];
                    arr[j] = arr[j + 1];
                    arr[j + 1] = temp;
                }
                j = j + 1;
            }
            i = i + 1;
        }

        print "Отсортированный массив:";
        print arr;
        """;

    public static int Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var vm = new VM();

        if (args.Length > 1)
        {
            Console.Error.WriteLine("Usage: umbra [script.umb | --test | --repl]");
            return 64;
        }

        if (args.Length == 1)
        {
            return args[0] switch
            {
                "--test" => RunScript(vm, AcceptanceTestScript),
                "--repl" => RunRepl(vm),
                _ => RunFile(vm, args[0])
            };
        }

        // Execute the built-in acceptance test suite by default when launched without arguments.
        var testResult = RunScript(vm, AcceptanceTestScript);

        // If running in an interactive terminal session, launch the interactive REPL afterwards.
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            return RunRepl(vm);
        }

        return testResult;
    }

    private static int RunFile(VM vm, string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: Could not find file '{path}'.");
            return 66;
        }

        var source = File.ReadAllText(path);
        return RunScript(vm, source);
    }

    private static int RunScript(VM vm, string source)
    {
        var result = vm.Interpret(source);
        return result switch
        {
            InterpretResult.Ok => 0,
            InterpretResult.CompileError => 65,
            InterpretResult.RuntimeError => 70,
            _ => 1
        };
    }

    private static int RunRepl(VM vm)
    {
        Console.WriteLine($"Umbra v{Version} REPL (type 'exit' or Ctrl+C to quit)");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();

            if (line is null)
            {
                Console.WriteLine();
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed is "exit" or "quit")
            {
                break;
            }

            vm.Interpret(line);
        }

        return 0;
    }
}
