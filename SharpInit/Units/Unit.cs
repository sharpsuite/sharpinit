using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public delegate void OnUnitStateChange(Unit source, UnitState next_state);

    /// <summary>
    /// Base unit class with shared functionality that all unit types must inherit from.
    /// </summary>
    public abstract class Unit
    {
        public string UnitName { get; set; }
        public UnitState CurrentState { get; internal set; }

        public UnitFile File { get => GetUnitFile(); }

        public event OnUnitStateChange UnitStateChange;
        
        protected Unit(string path)
        {
            LoadUnitFile(path);
            UnitName = File.UnitName;
        }

        protected Unit()
        {

        }

        public abstract UnitFile GetUnitFile();
        public abstract void LoadUnitFile(string path);
        
        internal void SetState(UnitState next_state)
        {
            UnitStateChange?.Invoke(this, next_state); // block while state changes are handled
                                                       // TODO: Investigate whether this could result in a deadlock

            CurrentState = next_state;
        }

        public abstract void Activate();
        public abstract void Deactivate();

        public void ReloadUnitFile()
        {
            LoadUnitFile(File.UnitPath);
            UnitName = File.UnitName;
        }
    }

    public enum UnitState
    {
        Inactive,
        Active,
        Activating,
        Deactivating,
        Failed
    }
}
