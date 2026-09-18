namespace LeoClassroom.Cli;

// Minimal console prompt helpers (no reflection-based command framework — AOT-friendly).
public static class Prompts
{
    public static string ReadRequired(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            string? value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
            Console.WriteLine("  (required)");
        }
    }

    public static string? ReadOptional(string label)
    {
        Console.Write($"{label} (optional, blank to skip): ");
        string? value = Console.ReadLine();

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static bool ReadYesNo(string label, bool defaultValue)
    {
        string suffix = defaultValue ? "[Y/n]" : "[y/N]";
        Console.Write($"{label} {suffix}: ");
        string? value = Console.ReadLine()?.Trim().ToLowerInvariant();

        return value switch
        {
            "y" or "yes" => true,
            "n" or "no" => false,
            _ => defaultValue
        };
    }

    public static int ReadChoice(string label, int count)
    {
        while (true)
        {
            Console.Write($"{label} (1-{count}): ");
            if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= count)
            {
                return choice;
            }
            Console.WriteLine("  invalid selection");
        }
    }

    public static DeadlineKind ReadDeadlineKind()
    {
        Console.WriteLine("Deadline kind: 1) none  2) soft  3) hard");
        return Prompts.ReadChoice("Choose", 3) switch
        {
            2 => DeadlineKind.Soft,
            3 => DeadlineKind.Hard,
            _ => DeadlineKind.None
        };
    }
}
