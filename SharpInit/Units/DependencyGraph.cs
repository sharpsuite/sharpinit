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

        /// <summary>
        /// Create an empty dependency graph.
        /// </summary>
        public DependencyGraph()
        {

        }

        /// <summary>
        /// Add a dependency to the dependency graph.
        /// </summary>
        /// <param name="dependency">The dependency to add.</param>
        public void AddDependency(T dependency)
        {
            Dependencies.Add(dependency);
        }

        /// <summary>
        /// Adds a range of dependencies to the dependency graph.
        /// </summary>
        /// <param name="dependencies">The dependencies to add.</param>
        public void AddDependencies(params IEnumerable<T>[] dependencies)
        {
            Dependencies.AddRange(dependencies.SelectMany(t => t));
        }

        /// <summary>
        /// Gathers a list of dependencies that involve a particular unit.
        /// </summary>
        /// <param name="unit">The unit of interest.</param>
        /// <returns>A list of all dependencies that involve <paramref name="unit"/>.</returns>
        public IEnumerable<T> GetDependencies(string unit)
        {
            return Dependencies.Where(d => d.LeftUnit == unit || d.RightUnit == unit);
        }

        /// <summary>
        /// Performs a breadth-first-traversal on the dependency graph, returns all edges found starting from a 
        /// particular unit. Optionally filters dependencies by <paramref name="filter"/>, or adds dependencies that are
        /// in "reverse".
        /// </summary>
        /// <param name="start_unit">The unit to start traversing the graph from.</param>
        /// <param name="filter">A filter predicate that can be used to search for a particular type of dependency.</param>
        /// <param name="add_reverse">If true, dependencies that mention a particular unit as the "right" unit will be included into the growing graph.</param>
        /// <returns>A list of all dependencies that have been hit by the traversal.</returns>
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

                relevant_dependencies = relevant_dependencies.Where(filter).ToList();

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
