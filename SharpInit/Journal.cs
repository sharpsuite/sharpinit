using Mono.Unix.Native;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using NLog;
using NLog.Targets;
using NLog.Config;

namespace SharpInit
{
    public class Journal
    {
        public event OnJournalData JournalDataReceived;

        private Logger Log = LogManager.GetCurrentClassLogger();

        private Dictionary<string, JournalBuffer> Buffers = new Dictionary<string, JournalBuffer>();

        public Journal()
        {
            this.JournalDataReceived += (s, e) => 
            {
                GetBuffer(e.Source).Add(e.Entry);

                if (e.Source != "main")
                    GetBuffer("main").Add(e.Entry);
            };
        }

        internal JournalBuffer GetBuffer(string name) => Buffers.ContainsKey(name) ? Buffers[name] : (Buffers[name] = new JournalBuffer(name));

        public IEnumerable<JournalEntry> Tail(string name, int lines = int.MaxValue) =>
            Buffers.ContainsKey(name) ? GetBuffer(name).Tail(lines) : new JournalEntry[0];
        internal void RaiseJournalData(string from, string message, int level = 2) =>
            RaiseJournalData(from, Encoding.UTF8.GetBytes(message), level);

        internal void RaiseJournalData(string from, byte[] data, int level = 2) =>
            RaiseJournalData(new JournalEntry() 
            { 
                Source = from, 
                RawMessage = data, 
                Message = Encoding.UTF8.GetString(data), 
                Created = DateTime.UtcNow, 
                LocalTime = Program.ElapsedSinceStartup().TotalSeconds,
                LogLevel = level
            });

        internal void RaiseJournalData(JournalEntry entry)
        {
            JournalDataReceived?.Invoke(this, new JournalDataEventArgs(entry));
        }
    }

    public class JournalBuffer
    {
        private List<JournalEntry> Entries = new List<JournalEntry>();

        public string Name { get; set; }
        public int Capacity { get; set; }

        public JournalBuffer(string name, int capacity = int.MaxValue)
        {
            Name = name;
            Capacity = capacity;
        }

        public void Add(JournalEntry entry)
        {
            Entries.Add(entry);

            if (Entries.Count > Capacity)
                Entries.RemoveAt(0);
        }

        public IEnumerable<JournalEntry> Tail(int number)
        {
            if (number <= 0)
                return Entries.ToList();
            
            return Entries.Skip(Entries.Count - number).ToList();
        }
    }

    public class JournalEntry
    {
        public string Source { get; set; }
        public DateTime Created { get; set; }
        public double LocalTime { get; set; }
        public string Message { get; set; }
        public byte[] RawMessage { get; set; }
        public int LogLevel { get; set; }

        public JournalEntry()
        { }
    }

    [Target("SharpInitJournal")]
    public sealed class JournalTarget : TargetWithLayout
    {

        public JournalTarget()
        {
        }

        public JournalTarget(Journal journal)
        {
            Journal = journal;
        }

        public Journal Journal { get; set; }
 
        protected override void Write(LogEventInfo log_event) 
        {
            var sources = NLog.NestedDiagnosticsLogicalContext.GetAllObjects();
            if (sources.Length == 0)
            {
                return;
            }

            NLog.NestedDiagnosticsLogicalContext.Clear();

            var unit_source = sources.First() as string;

            var rendered = this.Layout.Render(log_event);
            Journal.RaiseJournalData(unit_source, rendered, log_event.Level.Ordinal);
            for (int i = sources.Length - 1; i >= 0; i--)
                NLog.NestedDiagnosticsLogicalContext.PushObject(sources[i]);
        }
    }
}