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
        string projectPath = @"/home/joelvarghese/Code/carwaleweb/Carwale/Carwale.UI.csproj";
        string targetFileName = "ModelPageAdapter.cs"; 

        // 2. Register MSBuild
        MSBuildLocator.RegisterDefaults();

        // FORCE LOAD: Ensure C# workspace support is loaded
        var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);
        
        using var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;

        Console.WriteLine($"Loading project: {projectPath}...");
        // Handle loading errors gracefully to avoid crash on partial loads
        var project = await workspace.OpenProjectAsync(projectPath);

        // 3. Find the specific document
        var document = project.Documents.FirstOrDefault(d => d.Name.EndsWith(targetFileName));

        if (document == null)
        {
            Console.WriteLine($"Error: Could not find file '{targetFileName}' in project.");
            return;
        }

        Console.WriteLine($"Analyzing usage in: {document.Name}\n");

        // 4. Get Semantic Model
        var root = await document.GetSyntaxRootAsync();
        var model = await document.GetSemanticModelAsync();

        // Dictionary: Key = FilePath, Value = Set of things used (Methods/Classes)
        var fileDependencies = new Dictionary<string, HashSet<string>>();

        // 5. Traverse nodes
        var nodes = root.DescendantNodes().OfType<IdentifierNameSyntax>();

        foreach (var node in nodes)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;

            if (symbol == null) continue;

            // We want to find the file where this symbol (Method/Class/Prop) is DEFINED.
            // If it's a Method, ContainingType is the Class. 
            // If it's a Class, it IS the type.
            var definitionType = symbol is INamedTypeSymbol namedType ? namedType : symbol.ContainingType;

            if (definitionType == null) continue;

            // 6. Check if defined in Source Code (not metadata/DLL)
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

                    // Generate a readable name for what is being used
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
                string fileName = Path.GetFileName(kvp.Key);
                Console.WriteLine($"\nFile: {fileName}");
                // Console.WriteLine($"Path: {kvp.Key}"); // Uncomment for full path

                foreach (var usage in kvp.Value.OrderBy(x => x))
                {
                    Console.WriteLine($"  -> {usage}");
                }
            }
        }
    }

    // Helper to make the output readable (e.g. "Method(int, string)" instead of just "Method")
    static string GetReadableSymbolName(ISymbol symbol)
    {
        // For methods, we want the signature to see overloads
        if (symbol is IMethodSymbol methodSymbol)
        {
            return $"[Method] {methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
        }
        // For properties
        if (symbol is IPropertySymbol)
        {
            return $"[Property] {symbol.Name}";
        }
        // For classes/interfaces (e.g. variable declarations `MyClass c = ...`)
        if (symbol is INamedTypeSymbol)
        {
            return $"[Type] {symbol.Name}";
        }
        
        // Fallback
        return $"[{symbol.Kind}] {symbol.Name}";
    }
}