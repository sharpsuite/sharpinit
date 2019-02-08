using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    /// <summary>
    /// Represents a graph of dependencies with units as the vertices and dependencies as the edges.
    /// </summary>
    /// <typeparam name="T">The type of dependency represented by this graph (one of OrderingDependency or RequirementDependency)</typeparam>
    public class DependencyGraph<T>
        where T : Dependency
    {
        public List<T> Dependencies = new List<T>();

        public DependencyGraph()
        {

        }

        public void AddDependency(T dependency)
        {
            Dependencies.Add(dependency);
        }

        public void AddDependencies(params IEnumerable<T>[] dependencies)
        {
            Dependencies.AddRange(dependencies.SelectMany(t => t));
        }

        public IEnumerable<T> GetDependencies(string unit)
        {
            return Dependencies.Where(d => d.LeftUnit == unit || d.RightUnit == unit);
        }
    }
}
