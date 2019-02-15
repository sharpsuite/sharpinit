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

        public IEnumerable<T> TraverseDependencyGraph(string start_unit, Func<T, bool> filter, bool add_reverse = true)
        {
            string current_unit = start_unit;
            var ret = new List<T>();
            var nodes = new List<string>() { start_unit };
            var visited = new Dictionary<string, bool>();

            while(true)
            {
                var relevant_dependencies = GetDependencies(current_unit);

                if (!add_reverse)
                    relevant_dependencies = relevant_dependencies.Where(d => d.LeftUnit == current_unit);

                relevant_dependencies = relevant_dependencies.Where(filter);

                foreach(var dependency in relevant_dependencies)
                {
                    if (ret.Contains(dependency))
                        continue;

                    ret.Add(dependency);

                    var other_end = dependency.LeftUnit == current_unit ? dependency.RightUnit : dependency.LeftUnit;
                    nodes.Add(other_end);
                    visited[other_end] = false;
                }

                visited[current_unit] = true;

                if (!nodes.Any(n => !visited[n]))
                    return ret;

                current_unit = nodes.First(n => !visited[n]);
            }
        }
    }
}
