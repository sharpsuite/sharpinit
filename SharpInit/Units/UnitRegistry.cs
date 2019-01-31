using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpInit.Units
{
    public static class UnitRegistry
    {
        public static Dictionary<string, Unit> Units = new Dictionary<string, Unit>();
        public static Dictionary<string, Type> UnitTypes = new Dictionary<string, Type>();

        public static void AddUnit(Unit unit)
        {
            if (unit == null)
                return;

            if (Units.ContainsKey(unit.UnitName))
                throw new InvalidOperationException();

            Units[unit.UnitName] = unit;
        }

        public static void AddUnitByPath(string path) => AddUnit(CreateUnit(path));

        public static void ScanDirectory(string path, bool recursive = false)
        {
            var directories = recursive ? Directory.GetDirectories(path) : new string[0];
            var files = Directory.GetFiles(path);

            foreach (var file in files)
                AddUnitByPath(file);

            foreach (var dir in directories)
                ScanDirectory(dir, recursive);
        }

        public static Unit GetUnit(string name) => Units.ContainsKey(name) ? Units[name] : null;

        public static Unit CreateUnit(string path)
        {
            var ext = Path.GetExtension(path);

            if (!UnitTypes.ContainsKey(ext))
                return null;

            return (Unit)Activator.CreateInstance(UnitTypes[ext], path);
        }

        public static void InitializeTypes()
        {
            UnitTypes[".unit"] = typeof(Unit);
            UnitTypes[".service"] = typeof(ServiceUnit);
        }
    }
}
