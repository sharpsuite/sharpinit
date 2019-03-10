using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public class UnitStateChangeTransaction : Transaction
    {
        public Unit SourceUnit { get; set; }
        public List<Unit> AffectedUnits { get; set; }
        public Dictionary<Unit, List<string>> Reasoning { get; set; }

        public UnitStateChangeTransaction(Unit source_unit, List<Unit> units, Dictionary<Unit, List<string>> reasoning)
        {
            SourceUnit = source_unit;
            AffectedUnits = units;
            Reasoning = reasoning;
        }

        public UnitStateChangeTransaction(Unit source_unit, List<Unit> units) :
            this(source_unit, units, new Dictionary<Unit, List<string>>() { { source_unit, new List<string>() { $"{source_unit.UnitName} is changing state because it's being asked to" } } })
        {

        }

        public UnitStateChangeTransaction(Unit source_unit) :
            this(source_unit, new List<Unit>() { source_unit })
        {

        }
    }
}