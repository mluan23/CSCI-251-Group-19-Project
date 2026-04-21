// Aman Shah
// CSCI 251 - Secure Distributed Messenger
//
// SPRINT 3: P2P & Advanced Features
// Due: Week 14 | Work on: Weeks 11-13
//

using System.Text.Json;
using SecureMessenger.Core;

namespace SecureMessenger.UI;

// saves messages to a json file so we can view them later with /history
public class MessageHistory
{
    private readonly string _historyFile;
    private readonly List<Message> _messages = new();
    private readonly object _lock = new();

    public MessageHistory(string historyFile = "message_history.json")
    {
        _historyFile = historyFile;
        Load();
    }

    // add message to list + write to file
    public void SaveMessage(Message message)
    {
        lock (_lock)
        {
            _messages.Add(message);
            PersistToFile();
        }
    }

    // read history from file on startup so old messages show up
    public void Load()
    {
        try
        {
            if (!File.Exists(_historyFile)) return;

            string json = File.ReadAllText(_historyFile);
            var loaded = JsonSerializer.Deserialize<List<Message>>(json);
            if (loaded == null) return;

            lock (_lock)
            {
                _messages.Clear();
                _messages.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load history: {ex.Message}");
        }
    }

    // write everything to disk (called while holding the lock already)
    private void PersistToFile()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_messages, options);
            File.WriteAllText(_historyFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save history: {ex.Message}");
        }
    }

    // newest first, optionally capped to a limit
    public IEnumerable<Message> GetHistory(int? limit = null)
    {
        lock (_lock)
        {
            var ordered = _messages.OrderByDescending(m => m.Timestamp);
            if (limit.HasValue)
                return ordered.Take(limit.Value).ToList();
            return ordered.ToList();
        }
    }

    // print the messages in chat order (oldest -> newest)
    public void ShowHistory(int limit = 50)
    {
        Console.WriteLine($"--- Message History (last {limit} messages) ---");
        foreach (var message in GetHistory(limit).Reverse())
        {
            Console.WriteLine(message.ToString());
        }
        Console.WriteLine("--- End of History ---");
    }

    // wipe everything (not currently wired to a command but useful for testing)
    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
            if (File.Exists(_historyFile))
            {
                try { File.Delete(_historyFile); }
                catch (Exception ex) { Console.WriteLine($"Failed to delete history file: {ex.Message}"); }
            }
        }
    }
}
