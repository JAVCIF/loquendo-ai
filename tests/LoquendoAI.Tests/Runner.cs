using System.Diagnostics;
using System.Reflection;

namespace LoquendoAI.Tests;

/// <summary>Marks a test: a static method, void or Task, without parameters.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>Thrown to skip a test whose tool (FFmpeg) is not available.</summary>
internal sealed class SkipException(string reason) : Exception(reason);

internal static class Runner
{
    public static async Task<int> Main(string[] args)
    {
        // Tests must not read or write the user's caches and catalogs.
        Environment.SetEnvironmentVariable("LOQUENDO_AI_PROBE_CACHE", "0");
        var filter = args.FirstOrDefault() ?? "";
        var tests = typeof(Runner).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<TestAttribute>()))
            .Where(x => x.Attribute is not null)
            .Select(x => (x.Method, Name: $"{x.Method.DeclaringType!.Name}.{x.Method.Name}", x.Attribute!.Description))
            .Where(x => filter.Length == 0 || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        x.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        if (tests.Length == 0)
        {
            Console.WriteLine($"Ninguna prueba coincide con «{filter}».");
            return 1;
        }
        int passed = 0, failed = 0, skipped = 0;
        var total = Stopwatch.StartNew();
        foreach (var (method, name, description) in tests)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var result = method.Invoke(null, null);
                if (result is Task task) await task;
                passed++;
                Console.WriteLine($"PASS  {name} — {description} ({watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                var error = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                if (error is SkipException)
                {
                    skipped++;
                    Console.WriteLine($"SKIP  {name} — {error.Message}");
                    continue;
                }
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL  {name} — {description}");
                Console.ResetColor();
                Console.WriteLine("      " + error.GetType().Name + ": " + error.Message.Replace("\n", "\n      "));
                if (error is not AssertionException)
                    Console.WriteLine("      " + (error.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim());
            }
        }
        Console.WriteLine();
        Console.WriteLine($"{passed} correctas, {failed} fallidas, {skipped} omitidas ({total.Elapsed.TotalSeconds:0.0} s)");
        return failed;
    }
}

internal sealed class AssertionException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string what = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{what}: se esperaba «{expected}» y se obtuvo «{actual}»".TrimStart(':', ' '));
    }

    public static void Near(double expected, double actual, double tolerance, string what = "")
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new AssertionException($"{what}: se esperaba {expected} ± {tolerance} y se obtuvo {actual}".TrimStart(':', ' '));
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what = "")
    {
        var e = expected.ToArray();
        var a = actual.ToArray();
        if (!e.SequenceEqual(a))
            throw new AssertionException($"{what}: se esperaba [{string.Join(", ", e)}] y se obtuvo [{string.Join(", ", a)}]".TrimStart(':', ' '));
    }

    public static void Contains(string expected, string actual, string what = "")
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new AssertionException($"{what}: «{actual}» no contiene «{expected}»".TrimStart(':', ' '));
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string what) where TException : Exception
    {
        try { await action(); }
        catch (TException ex) { return ex; }
        catch (Exception ex) { throw new AssertionException($"{what}: se esperaba {typeof(TException).Name} y se lanzó {ex.GetType().Name}: {ex.Message}"); }
        throw new AssertionException($"{what}: se esperaba {typeof(TException).Name} y no se lanzó nada");
    }
}
