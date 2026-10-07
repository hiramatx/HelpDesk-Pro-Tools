using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Step-by-step timings of one PC Details load, appended to <see cref="LogPath"/> when the load ends.
/// The trace and the current section flow with async calls (AsyncLocal), so a WMI query or registry
/// read made anywhere under a section is logged under that section without passing anything around.
/// </summary>
public sealed class LoadTrace
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelpDeskProTools", "Logs", "pc-details.log");

    // Past this size the log is moved to pc-details.old.log and a new one is started.
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly object WriteLock = new();

    private static readonly AsyncLocal<LoadTrace?> CurrentTrace = new();
    private static readonly AsyncLocal<string?> CurrentSection = new();

    private const string SharedSection = "Shared queries";
    private const string WindowSection = "Window";

    private readonly string _pc;
    private readonly DateTime _started = DateTime.Now;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<Entry> _entries = new();
    private readonly ConcurrentDictionary<(string Section, string Step), Tally> _tallies = new();

    /// <summary>One timed step; an empty <see cref="Step"/> is the whole section.</summary>
    private sealed record Entry(string Section, string Step, TimeSpan Start, TimeSpan End, string? Error)
    {
        public TimeSpan Took => End - Start;
    }

    /// <summary>Many small calls of the same kind (registry reads), summed so the log stays short.</summary>
    private sealed class Tally
    {
        public int Count, Errors;
        public TimeSpan Total, Longest, First = TimeSpan.MaxValue, Last;
    }

    public LoadTrace(string pc) => _pc = pc;

    private TimeSpan Now => _clock.Elapsed;

    /// <summary>Makes this the trace of the calling async flow and of everything it starts from here on.</summary>
    public void Attach() => CurrentTrace.Value = this;

    /// <summary>Names the section that the following steps of this async flow are logged under.</summary>
    public static void EnterSection(string name) => CurrentSection.Value = name;

    private static string SectionName => CurrentSection.Value ?? SharedSection;

    /// <summary>Records a whole section that started at <paramref name="start"/> (from <see cref="Elapsed"/>).</summary>
    public static void SectionDone(string name, TimeSpan start, string? error)
    {
        if (CurrentTrace.Value is { } trace)
            trace._entries.Enqueue(new Entry(name, "", start, trace.Now, error));
    }

    /// <summary>Time since the load started, or zero when no trace is attached.</summary>
    public static TimeSpan Elapsed => CurrentTrace.Value?.Now ?? TimeSpan.Zero;

    /// <summary>Runs <paramref name="run"/> and logs how long it took under the current section.</summary>
    public static T Step<T>(string step, Func<T> run)
    {
        var trace = CurrentTrace.Value;
        if (trace is null) return run();

        var start = trace.Now;
        try
        {
            var result = run();
            trace._entries.Enqueue(new Entry(SectionName, step, start, trace.Now, null));
            return result;
        }
        catch (Exception ex)
        {
            trace._entries.Enqueue(new Entry(SectionName, step, start, trace.Now, ex.Message));
            throw;
        }
    }

    public static void Step(string step, Action run) => Step(step, () => { run(); return true; });

    /// <summary>Awaits a task another section started and logs how long this section waited for it.</summary>
    public static async Task<T> WaitAsync<T>(string what, Task<T> task)
    {
        var trace = CurrentTrace.Value;
        if (trace is null || task.IsCompleted) return await task;

        var start = trace.Now;
        try
        {
            return await task;
        }
        finally
        {
            trace._entries.Enqueue(new Entry(SectionName, $"waiting for {what}", start, trace.Now, null));
        }
    }

    /// <summary>Like <see cref="Step{T}"/> for calls made many times: logged as one line with a count and the total.</summary>
    public static T Count<T>(string step, Func<T> run)
    {
        var trace = CurrentTrace.Value;
        if (trace is null) return run();

        var start = trace.Now;
        var failed = false;
        try
        {
            return run();
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var end = trace.Now;
            var tally = trace._tallies.GetOrAdd((SectionName, step), _ => new Tally());
            lock (tally)
            {
                tally.Count++;
                if (failed) tally.Errors++;
                tally.Total += end - start;
                if (end - start > tally.Longest) tally.Longest = end - start;
                if (start < tally.First) tally.First = start;
                if (end > tally.Last) tally.Last = end;
            }
        }
    }

    /// <summary>Marks a moment in the window (e.g. a card being shown).</summary>
    public void Mark(string what) => _entries.Enqueue(new Entry(WindowSection, what, Now, Now, null));

    /// <summary>Appends this load to the log file. Never throws: logging must not break PC Details.</summary>
    public void Write(string? failure = null)
    {
        try
        {
            var text = Format(failure);
            lock (WriteLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                    File.Move(LogPath, Path.ChangeExtension(LogPath, ".old.log"), overwrite: true);
                File.AppendAllText(LogPath, text);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not write {LogPath}: {ex.Message}");
        }
    }

    private string Format(string? failure)
    {
        var entries = _entries.ToList();
        foreach (var ((section, step), t) in _tallies)
        {
            lock (t)
            {
                var errors = t.Errors > 0 ? $", {t.Errors} failed" : "";
                entries.Add(new Entry(section, $"{step}: {t.Count} calls, longest {t.Longest.TotalSeconds:0.00} s{errors}",
                    t.First, t.First + t.Total, null));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"===== {_started:yyyy-MM-dd HH:mm:ss}  PC Details for {_pc}  total {Now.TotalSeconds:0.00} s =====");
        if (failure is not null) sb.AppendLine($"Load failed: {failure}");
        sb.AppendLine("  start     end    took   (seconds since the window started loading; a call count's 'took' is the sum of its calls)");

        // Sections in the order they started, each followed by its steps; the window's marks come last.
        var groups = entries
            .GroupBy(e => e.Section)
            .OrderBy(g => g.Key == WindowSection)
            .ThenBy(g => g.Min(e => e.Start));
        foreach (var group in groups)
        {
            var whole = group.FirstOrDefault(e => e.Step.Length == 0);
            var start = whole?.Start ?? group.Min(e => e.Start);
            var end = whole?.End ?? group.Max(e => e.End);
            sb.AppendLine(Line(start, end, group.Key, whole?.Error));
            foreach (var e in group.Where(e => e.Step.Length > 0).OrderBy(e => e.Start))
                sb.AppendLine(Line(e.Start, e.End, "    " + e.Step, e.Error));
        }

        var slowest = entries.Where(e => e.Step.Length > 0 && e.Section != WindowSection)
            .OrderByDescending(e => e.Took).Take(5).ToList();
        if (slowest.Count > 0)
        {
            sb.AppendLine("Slowest steps:");
            foreach (var e in slowest)
                sb.AppendLine($"  {e.Took.TotalSeconds,6:0.00} s  {e.Section}: {e.Step}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Line(TimeSpan start, TimeSpan end, string label, string? error) =>
        $"{start.TotalSeconds,7:0.00} {end.TotalSeconds,7:0.00} {(end - start).TotalSeconds,7:0.00}  {label}" +
        (error is null ? "" : $"  FAILED: {error}");
}
