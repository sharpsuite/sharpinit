using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using SharpInit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpInit.Coverage
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var property_support = (await GetSupportedPropertyPaths("../"));
            
            // Add some predefined supported properties.
            var supported_listens = new [] { "Stream", "Datagram", "SequentialPacket", "FIFO"};
            var supported_conditionals = new [] { "" };

            SharpInit.Units.UnitConditions.BuildConditionCache();
            supported_conditionals = SharpInit.Units.UnitConditions.ConditionCache.Keys.ToArray();

            foreach (var listen in supported_listens)
                property_support[$"Socket/Listen{listen}"] = int.MaxValue;

            foreach (var conditional in supported_conditionals)
            {
                property_support[$"Unit/Condition{conditional}"] = int.MaxValue;
                property_support[$"Unit/Assert{conditional}"] = int.MaxValue;
            }

            var property_support_invariant = property_support.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value);
            
            var total_properties = property_support.Count();
            var supported_properties = property_support.Count(pair => pair.Value > 0);

            Console.WriteLine($"{supported_properties} keys supported out of {total_properties} defined: {100d * (double)supported_properties / (double)total_properties:0.00}% coverage");

            if (!args.Any(File.Exists))
                return;

            Dictionary<string, int> unsupported_count = new Dictionary<string, int>();

            var analyzed_files = args.Where(File.Exists).Select(unit_file =>
            {
                var supported_statements = 0;
                var unsupported_statements = 0;
                var unrecognized_statements = 0;
                var total_statements = 0;

                try 
                {
                    var parsed_file = SharpInit.Units.UnitParser.ParseFile(unit_file);
                    
                    foreach (var prop in parsed_file.Properties)
                    {
                        total_statements++;

                        if (!property_support_invariant.ContainsKey(prop.Key.ToLowerInvariant()))
                        {
                            unrecognized_statements++;
                            unsupported_count[prop.Key] = (unsupported_count.ContainsKey(prop.Key) ? unsupported_count[prop.Key] : 0) + 1;
                        }
                        else if (property_support[prop.Key] == 0)
                        {
                            unsupported_statements++;
                            unsupported_count[prop.Key] = (unsupported_count.ContainsKey(prop.Key) ? unsupported_count[prop.Key] : 0) + 1;
                        }
                        else
                            supported_statements++;
                    }
                }
                catch {}

                return new
                {
                    Path = unit_file,
                    TotalStatements = total_statements,
                    SupportedStatements = supported_statements,
                    UnsupportedStatements = unsupported_statements,
                    UnrecognizedStatements = unrecognized_statements
                };
            }).Where(f => f.TotalStatements > 0).ToList();

            const int filename_width = 35;
            const int column_width = 15;
            const int row_width = filename_width + (column_width * 5);
            
            Console.WriteLine();
            Console.WriteLine($"Analyzed {analyzed_files.Count} unit files.");
            Console.WriteLine();
            Console.WriteLine($"{"File",-filename_width}{"Coverage",column_width}{"Supported",column_width}{"Unsupported",column_width}{"Unrecognized",column_width}{"Total",column_width}");
            Console.WriteLine(new string('-', row_width));
            Console.WriteLine();

            foreach (var file in analyzed_files)
            {
                var coverage_percent = (double)file.SupportedStatements / (double)file.TotalStatements;
                Console.WriteLine($"{StringEscaper.Truncate(Path.GetFileName(file.Path), filename_width),-filename_width}{coverage_percent,column_width:0.00%}{file.SupportedStatements,column_width}{file.UnsupportedStatements,column_width}{file.UnrecognizedStatements,column_width}{file.TotalStatements,column_width}");
            }

            Console.WriteLine();
            Console.WriteLine(new string('-', row_width));

            var total_coverage = (double)analyzed_files.Sum(f => f.SupportedStatements) / (double)analyzed_files.Sum(f => f.TotalStatements);
            var fully_supported_count = analyzed_files.Count(f => f.SupportedStatements == f.TotalStatements);

            Console.WriteLine($"{"Total",-filename_width}{total_coverage,column_width:0.00%}{analyzed_files.Sum(f => f.SupportedStatements),column_width}{analyzed_files.Sum(f => f.UnsupportedStatements),column_width}{analyzed_files.Sum(f => f.UnrecognizedStatements),column_width}{analyzed_files.Sum(f => f.TotalStatements),column_width}");
            Console.WriteLine();
            Console.WriteLine($"{fully_supported_count}/{analyzed_files.Count} ({(double)fully_supported_count / (double)analyzed_files.Count:0.00%}) unit files with total coverage.");
            Console.WriteLine();
            Console.WriteLine();

            if (!unsupported_count.Any())
            {
                Console.WriteLine($"Hooray! No unsupported keys encountered.");
            }
            else
            {
                Console.WriteLine($"Most frequent unsupported keys ({unsupported_count.Count} total):");
                Console.WriteLine();

                Console.WriteLine($"{"Property path",-filename_width}{"Occurrences",column_width}");
                Console.WriteLine(new string('-', filename_width + column_width));
                foreach(var pair in unsupported_count.OrderByDescending(p => p.Value).Take(20))
                {
                    Console.WriteLine($"{pair.Key,-filename_width}{pair.Value,column_width}");
                }
            }

            Console.WriteLine();
        }

        static async Task<Dictionary<string, int>> GetSupportedPropertyPaths(string sharpinit_root_dir)
        {
            var workspace = MSBuildWorkspace.Create();
            var w = new AdhocWorkspace();
            var solution = await workspace.OpenSolutionAsync(Path.Combine(sharpinit_root_dir, "SharpInit.sln"));
            
            foreach (var pt in solution.Projects)
            {
                var pq = w.AddProject(pt.Name, pt.Language);
                foreach (var r in pt.MetadataReferences)
                    pq = pq.AddMetadataReference(r);
                pq = pq.WithAllSourceFiles(pt);
                w.TryApplyChanges(pq.Solution);
            }

            solution = w.CurrentSolution;
            var project = solution.Projects.FirstOrDefault(p => p.Name == "SharpInit");

            if (project == default)
                throw new Exception($"Could not find project");
            
            var compilations = await Task.WhenAll(solution.Projects.Select(x => x.GetCompilationAsync()));
            var compilation = compilations.First(c => c.Assembly.Name == "SharpInit");
     
            List<IPropertySymbol> property_symbols = new List<IPropertySymbol>();
            
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var descendants = semanticModel.SyntaxTree.GetRoot().DescendantNodes();

                foreach (var descendant in descendants)
                {
                    if (!(descendant is ClassDeclarationSyntax))
                        continue;
                    
                    var @class = (ClassDeclarationSyntax)descendant;

                    if (@class == null || @class.Parent == null ||
                        !(@class.Parent is NamespaceDeclarationSyntax namespace_syntax) ||
                        !(namespace_syntax.Name is QualifiedNameSyntax namespace_name_syntax) ||
                        !(namespace_name_syntax.Left is IdentifierNameSyntax left_identifier) ||
                        !(namespace_name_syntax.Right is IdentifierNameSyntax right_identifier) ||
                        left_identifier.Identifier.ValueText != "SharpInit" || right_identifier.Identifier.ValueText != "Units" ||
                        !@class.Identifier.ValueText.EndsWith("Descriptor"))
                        continue;
                    
                    var symbol = semanticModel.GetDeclaredSymbol(@class);
                    property_symbols.AddRange(symbol.GetMembers().OfType<IPropertySymbol>().Where(prop => prop.GetAttributes().Any(attrib => attrib.AttributeClass.Name == "UnitPropertyAttribute")));
                }
            }

            var reference_count = property_symbols.SelectMany(symbol => 
            {
                var resolved_property_paths = new List<string>();
                var shorthand_property_path = ((((symbol.GetAttributes()[0].ApplicationSyntaxReference as SyntaxReference).GetSyntax() as AttributeSyntax).ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax).Token).ValueText;

                var shorthand_property_parts = shorthand_property_path.Split('/');
                if (shorthand_property_parts[1] == "@")
                    shorthand_property_parts[1] = symbol.Name;

                if (shorthand_property_parts[0] == "@")
                {
                    var descriptor_kind = symbol.ContainingType.Name;
                    descriptor_kind = descriptor_kind.Substring(0, descriptor_kind.IndexOf("UnitDescriptor"));

                    if (descriptor_kind == "")
                        descriptor_kind = "Unit";
                    
                    if (descriptor_kind == "Exec")
                    {
                        resolved_property_paths.Add($"Service/{shorthand_property_parts[1]}");
                        resolved_property_paths.Add($"Mount/{shorthand_property_parts[1]}");
                        resolved_property_paths.Add($"Socket/{shorthand_property_parts[1]}");
                        resolved_property_paths.Add($"Mount/{shorthand_property_parts[1]}");
                    }
                    else
                    {
                        resolved_property_paths.Add($"{descriptor_kind}/{shorthand_property_parts[1]}");
                    }
                }
                else
                    resolved_property_paths.Add($"{shorthand_property_parts[0]}/{shorthand_property_parts[1]}");
                
                var references = SymbolFinder.FindReferencesAsync(symbol, solution).Result.ToList();

                return resolved_property_paths.Select(path => new
                { 
                    Name = symbol.ToDisplayString(), 
                    FilePropertyName = path,
                    References = references,
                    ReferenceCount = references.Sum(@ref => @ref.Locations.Count()),
                });
            }).ToList();

            return reference_count.GroupBy(sym => sym.FilePropertyName).Select(sym => new KeyValuePair<string, int>(sym.Key, sym.Sum(r => r.ReferenceCount))).ToDictionary(p => p.Key, p => p.Value);
        }
    }

    static class ProjectExtensions
    {
        public static Project AddDocuments(this Project project, IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                project = project.AddDocument(file, File.ReadAllText(file)).Project;
            }
            return project;
        }

        private static IEnumerable<string> GetAllSourceFiles(string directoryPath)
        {
            var res = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
            return res;
        }


        public static Project WithAllSourceFiles(this Project project)
        {
            string projectDirectory = Directory.GetParent(project.FilePath).FullName;
            var files = GetAllSourceFiles(projectDirectory);
            var newProject = project.AddDocuments(files);
            return newProject;
        }

        public static Project WithAllSourceFiles(this Project project, Project other)
        {
            string projectDirectory = Directory.GetParent(other.FilePath).FullName;
            var files = GetAllSourceFiles(projectDirectory);
            var newProject = project.AddDocuments(files);
            return newProject;
        }
    }
}
