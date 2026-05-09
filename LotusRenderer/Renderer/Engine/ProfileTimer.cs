using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

public class TimeEntry
{
    public string Name;
    public double DurationMs;
    public long StartTick;
    public List<TimeEntry> Children = new(); // Tree structure for nesting
}

public static class ProfileTimer
{
    private const int LAST_X_AVG_ITERATIONS = 10;
    
    // We store only Root entries here. Children live inside parents.
    private static List<TimeEntry> _rootEntries = new();
    private static Stack<TimeEntry> _entryStack = new();

    // for each profile region id, an array of the last LAST_X_AVG_ITERATIONS recorded
    // times, so we can average them later
    // I considered a stack array instead of a list but would need to copy it in out
    // so let's see how this one performs, it should be fine it's small lists
    private static Dictionary<string, List<double>> _timesById = new();
    
    public static Dictionary<string, double> averagesById = new();
    
    public static void StartRegion(string regionName)
    {
        // init averager if this id is empty
        if (!_timesById.TryGetValue(regionName, out var list))
        {
            list = new List<double>();
            _timesById[regionName] = list;
            averagesById[regionName] = 0.0;
        }
        
        // start region
        var entry = new TimeEntry
        {
            Name = regionName,
            StartTick = Stopwatch.GetTimestamp() // High-res tick
        };

        if (_entryStack.Count > 0)
        {
            // If we are inside another region, add this as a child
            _entryStack.Peek().Children.Add(entry);
        }
        else
        {
            // If stack is empty, this is a root region
            _rootEntries.Add(entry);
        }
        
        _entryStack.Push(entry);
    }

    public static void EndRegion()
    {
        if (_entryStack.Count == 0) return;

        var entry = _entryStack.Pop();
        long endTick = Stopwatch.GetTimestamp();
        
        // Convert ticks to milliseconds
        entry.DurationMs = (endTick - entry.StartTick) * 1000.0 / Stopwatch.Frequency;
        _timesById[entry.Name].Add(entry.DurationMs);
    }

    public static void ResetAverages()
    {
        foreach (var kvp in _timesById)
        {
            _timesById[kvp.Key].Clear(); 
            averagesById[kvp.Key] = 0.0;
        }
    }

    public static string GetFrameTimingAndReset()
    {
        if (_entryStack.Count != 0)
             return "Error: Unbalanced Start/End Region calls.";
        
        CalculateAverages();
        

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== CPU Frame Profile ===");
        
        foreach (var entry in _rootEntries)
        {
            PrintEntryRecursive(sb, entry, 0);
        }

        _rootEntries.Clear();
        return sb.ToString();
    }

    private static void CalculateAverages()
    {
        foreach (var kvp in _timesById)
        {
            double avg = 0.0;
            var timesList = kvp.Value;
            
            if(timesList.Count > LAST_X_AVG_ITERATIONS)
                timesList.RemoveAt(0);
            
            for (int i = 0; i < timesList.Count; i++)
            {
                avg += timesList[i]; 
            }
            
            avg /= (double)timesList.Count;
            averagesById[kvp.Key] = avg;
        }
    }

    private static void PrintEntryRecursive(StringBuilder sb, TimeEntry entry, int depth)
    {
        // Indentation based on depth
        string indent = new string(' ', depth * 2);
        string branch = depth > 0 ? "└ " : "";
        
        sb.AppendLine($"{indent}{branch}{entry.Name}: {averagesById[entry.Name]:F2} ms");

        foreach (var child in entry.Children)
        {
            PrintEntryRecursive(sb, child, depth + 1);
        }
    }
}

public readonly struct ProfileRegion : IDisposable
{
    public static ProfileRegion Start(string name) => new ProfileRegion(name);
    private ProfileRegion(string name) => ProfileTimer.StartRegion(name);
    public void Dispose() => ProfileTimer.EndRegion();
}