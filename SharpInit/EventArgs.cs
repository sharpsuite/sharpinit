using System;

using SharpInit.Units;

namespace SharpInit
{
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