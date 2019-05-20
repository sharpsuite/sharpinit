using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    /// <summary>
    /// A generic dependency that represents an edge on the dependency graph. Used as a base
    /// for both requirement and ordering dependencies.
    /// </summary>
    /// <seealso cref="SharpInit.Units.RequirementDependency"/>
    /// <seealso cref="SharpInit.Units.OrderingDependency"/>
    public abstract class Dependency
    {
        public abstract DependencyType Type { get; }
        
        public string LeftUnit { get; set; }
        public string RightUnit { get; set; }

        public string SourceUnit { get; set; }
    }

    public enum DependencyType
    {
        Ordering, Requirement
    }
}
