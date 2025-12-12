using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. CONFIGURATION: Set your paths here
        // Point this to your .SLN file now
        string solutionPath = @"/home/joelvarghese/Code/carwaleweb/Monorepo.sln";
        string targetFileName = "ModelPageAdapter.cs"; 

        // 2. Register MSBuild
        MSBuildLocator.RegisterDefaults();

        // FORCE LOAD: Ensure C# workspace support is loaded
        var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);
        
        using var workspace = MSBuildWorkspace.Create();
        
        // This is crucial for large solutions to avoid crashing on unknown project types (like installers)
        workspace.SkipUnrecognizedProjects = true;

        Console.WriteLine($"Loading solution: {solutionPath}...");
        Console.WriteLine("This may take a moment for large solutions...");

        // Open the Solution instead of the Project
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        // 3. Find the specific document across ALL projects in the solution
        Document document = null;
        
        foreach (var proj in solution.Projects)
        {
            var doc = proj.Documents.FirstOrDefault(d => d.Name.EndsWith(targetFileName));
            if (doc != null)
            {
                document = doc;
                Console.WriteLine($"\nFound file '{targetFileName}' in project: {proj.Name}");
                break; // Stop at the first match
            }
        }

        if (document == null)
        {
            Console.WriteLine($"Error: Could not find file '{targetFileName}' in any project within the solution.");
            return;
        }

        Console.WriteLine($"Analyzing usage in: {document.Name}\n");

        // 4. Get Semantic Model
        var root = await document.GetSyntaxRootAsync();
        var model = await document.GetSemanticModelAsync();

        // Dictionary: Key = FilePath, Value = Set of things used
        var fileDependencies = new Dictionary<string, HashSet<string>>();

        // 5. Traverse nodes
        var nodes = root.DescendantNodes().OfType<IdentifierNameSyntax>();

        foreach (var node in nodes)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;

            if (symbol == null) continue;

            var definitionType = symbol is INamedTypeSymbol namedType ? namedType : symbol.ContainingType;

            if (definitionType == null) continue;

            // 6. Check where this is defined
            if (definitionType.DeclaringSyntaxReferences.Any())
            {
                var syntaxRef = definitionType.DeclaringSyntaxReferences.First();
                var originalFilePath = syntaxRef.SyntaxTree.FilePath;

                // Exclude self-references
                if (originalFilePath != document.FilePath)
                {
                    if (!fileDependencies.ContainsKey(originalFilePath))
                    {
                        fileDependencies[originalFilePath] = new HashSet<string>();
                    }

                    string usageName = GetReadableSymbolName(symbol);
                    fileDependencies[originalFilePath].Add(usageName);
                }
            }
        }

        // 7. Output Results
        Console.WriteLine("Found External Dependencies & Usages:");
        
        if (fileDependencies.Count == 0)
        {
            Console.WriteLine(" - No external source dependencies found.");
        }
        else
        {
            foreach (var kvp in fileDependencies)
            {
                // string fileName = Path.GetFileName(kvp.Key);
                string fileName = kvp.Key;
                // We display the file name AND the project folder name to help distinguish common files
                string folderName = Path.GetFileName(Path.GetDirectoryName(kvp.Key));
                
                Console.WriteLine($"\nFile: {fileName} (in {folderName})");

                foreach (var usage in kvp.Value.OrderBy(x => x))
                {
                    Console.WriteLine($"  -> {usage}");
                }
            }
        }
    }

    static string GetReadableSymbolName(ISymbol symbol)
    {
        if (symbol is IMethodSymbol methodSymbol)
        {
            return $"[Method] {methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
        }
        if (symbol is IPropertySymbol)
        {
            return $"[Property] {symbol.Name}";
        }
        if (symbol is INamedTypeSymbol)
        {
            return $"[Type] {symbol.Name}";
        }
        return $"[{symbol.Kind}] {symbol.Name}";
    }
}