using System;
using System.Text;

using SharpInit.Units;
using SharpInit.Platform.Unix;

namespace SharpInit
{
    public delegate void OnMountChanged(object sender, MountChangedEventArgs e);

    public class MountChangedEventArgs : EventArgs
    {
        public UnixMount Mount { get; set; }
        public MountChange Change { get; set; }

        public MountChangedEventArgs(UnixMount mount, MountChange change)
        {
            Mount = mount;
            Change = change;
        }
    }

    public enum MountChange
    {
        Added,
        Changed,
        Removed
    }

    public delegate void OnJournalData(object sender, JournalDataEventArgs e);

    public class JournalDataEventArgs : EventArgs
    {
        public string Source => Entry.Source;
        public string Data => Entry.Message;

        public JournalEntry Entry { get; set; }

        public JournalDataEventArgs(JournalEntry entry)
        {
            Entry = entry;
        }
    }

    public delegate void OnUnitStateChange(object sender, UnitStateChangeEventArgs e);

    public class UnitStateChangeEventArgs : EventArgs
    {
        public Unit Unit { get; set; }
        public UnitState NextState { get; set; }
        public string Reason { get; set; }

        public UnitStateChangeEventArgs(Unit unit, UnitState next_state, string reason)
        {
            Unit = unit;
            NextState = next_state;
            Reason = reason;
        }
    }
}