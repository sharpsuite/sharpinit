using System;
using System.Text;

using SharpInit.Units;
using SharpInit.Platform.Unix;

namespace SharpInit
{
    public delegate void OnBusNameReleased(object sender, BusNameReleasedEventArgs e);
    public class BusNameReleasedEventArgs : EventArgs
    {
        public string BusName { get; set; }
        public string PreviousOwner { get; set; }
        public Unit Unit { get; set; }

        public BusNameReleasedEventArgs(string bus_name, string previous_owner, Unit unit)
        {
            BusName = bus_name;
            PreviousOwner = previous_owner;
            Unit = unit;
        }
    }

    public delegate void OnDeviceAdded(object sender, DeviceAddedEventArgs e);
    public class DeviceAddedEventArgs : EventArgs
    {
        public string DevicePath { get; set; }
        
        public DeviceAddedEventArgs(string dev_path)
        {
            DevicePath = dev_path;
        }
    }
    public delegate void OnDeviceRemoved(object sender, DeviceRemovedEventArgs e);
    public class DeviceRemovedEventArgs : EventArgs
    {
        public string DevicePath { get; set; }
        
        public DeviceRemovedEventArgs(string dev_path)
        {
            DevicePath = dev_path;
        }
    }
    
    public delegate void OnDeviceUpdated(object sender, DeviceUpdatedEventArgs e);
    public class DeviceUpdatedEventArgs : EventArgs
    {
        public string DevicePath { get; set; }
        
        public DeviceUpdatedEventArgs(string dev_path)
        {
            DevicePath = dev_path;
        }
    }

    public delegate void OnUnitConfigurationChanged(object sender, UnitConfigurationChangedEventArgs e);
    public class UnitConfigurationChangedEventArgs : EventArgs
    {
        public Unit Unit { get; set; }

        public UnitConfigurationChangedEventArgs(Unit unit)
        {
            Unit = unit;
        }
    }

    public delegate void OnServiceProcessStart(object sender, ServiceProcessStartEventArgs e);
    public class ServiceProcessStartEventArgs : EventArgs
    {
        public Unit Unit { get; set; }
        public ProcessInfo Process { get; set; }

        public ServiceProcessStartEventArgs(Unit unit, ProcessInfo process)
        {
            Unit = unit;
            Process = process;
        }
    }

    public delegate void OnServiceProcessExit(object sender, ServiceProcessExitEventArgs e);
    public class ServiceProcessExitEventArgs : EventArgs
    {
        public Unit Unit { get; set; }
        public ProcessInfo Process { get; set; }
        public int ExitCode { get; set; }

        public ServiceProcessExitEventArgs(Unit unit, ProcessInfo process, int exit_code = -1)
        {
            Unit = unit;
            Process = process;

            ExitCode = exit_code == -1 ? process.ExitCode : exit_code;
        }
    }

    public delegate void OnUnitAdded(object sender, UnitAddedEventArgs e);
    public class UnitAddedEventArgs : EventArgs
    {
        public Unit Unit { get; set; }

        public UnitAddedEventArgs(Unit unit)
        {
            Unit = unit;
        }
    }

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

    public delegate void OnUnitStateChanged(object sender, UnitStateChangedEventArgs e);

    public class UnitStateChangedEventArgs : EventArgs
    {
        public Unit Unit { get; set; }
        public UnitState NextState { get; set; }
        public string Reason { get; set; }

        public UnitStateChangedEventArgs(Unit unit, UnitState next_state, string reason)
        {
            Unit = unit;
            NextState = next_state;
            Reason = reason;
        }
    }
}