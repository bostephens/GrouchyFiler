using System.Text;

namespace GrouchyFiler.Services;

// One bounded history shared by the display and exports. No separate pending queue.
internal sealed class RecentLog
{
    internal const int MaxEntries = 5000;
    internal const int MaxCharacters = 1_000_000;
    internal const int MaxEntryCharacters = 8192;
    private const int NoticeAllowance = 160;
    private readonly object gate = new();
    private readonly Queue<string> entries = new();
    private int characters;
    private long version, discarded;

    internal void Add(string message)
    {
        string prefix = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  ";
        const string truncated = " [truncated]";
        int available = MaxEntryCharacters - prefix.Length - Environment.NewLine.Length;
        if (message.Length > available) message = message[..(available - truncated.Length)] + truncated;
        string entry = prefix + message + Environment.NewLine;
        lock (gate)
        {
            entries.Enqueue(entry);
            characters += entry.Length;
            while (entries.Count > MaxEntries || characters > MaxCharacters - NoticeAllowance)
            {
                characters -= entries.Dequeue().Length;
                discarded++;
            }
            version++;
        }
    }

    internal Snapshot? Read(long knownVersion = -1, int characterLimit = MaxCharacters)
    {
        lock (gate)
        {
            if (version == knownVersion) return null;
            var lines = entries.ToArray();
            // Reserve space for a retention notice so the entire snapshot stays bounded.
            int budget = Math.Max(0, Math.Min(characterLimit, MaxCharacters) - NoticeAllowance);
            int start = lines.Length, length = 0;
            while (start > 0 && length + lines[start - 1].Length <= budget)
                length += lines[--start].Length;
            var text = new StringBuilder(length + NoticeAllowance);
            if (discarded > 0 || start > 0)
                text.AppendLine($"[Log] Showing recent history; {discarded + start:N0} older entries omitted. Save Log exports all retained entries.");
            for (int i = start; i < lines.Length; i++) text.Append(lines[i]);
            return new Snapshot(version, text.ToString());
        }
    }

    internal sealed record Snapshot(long Version, string Text);
}
