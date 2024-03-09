namespace SourceGenerators;

using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// A source generator to find all implementations of IGraphicsStateOperation and build a mapping dictionary out of the types
[Generator]
public class GraphicsStateOperations : ISourceGenerator
{
    // The interface we're interested in
    private const string OperationInterfaceName = "IGraphicsStateOperation";

    public void Initialize(GeneratorInitializationContext context) { }

    public void Execute(GeneratorExecutionContext context)
    {
        var allGraphicsOperations = GetGraphicOperations(context.Compilation);

        SourceText opsSource = SourceText.From(BuildOpsDictionary(allGraphicsOperations), Encoding.UTF8);
        context.AddSource("ReflectionGraphicsStateOperationFactory", opsSource);
    }

    // Build a dictionary of graphics operations inside the ReflectionGraphicsStateOperationFactory class
    private static string BuildOpsDictionary(IEnumerable<OperationAndTypeName> allOps)
    {
        var sb = new StringBuilder(@"
using System;
using System.Collections.Generic;

namespace UglyToad.PdfPig.Graphics
{
    internal partial class ReflectionGraphicsStateOperationFactory
    {
        private readonly IReadOnlyDictionary<string, Type> operations = 
            new Dictionary<string, Type>
            {
");

        foreach (var op in allOps)
        {
            // One of the operation symbols is a single quote - we have to escape that when creating the dictionary literal
            sb.AppendLine(
$"              [\"{op.Operation.Replace("\"", "\\\"")}\"] = typeof({op.TypeName}),"
            );
        }

        sb.Append(@"
            };
    }
}");
        return sb.ToString();
    }

    // Get the set of graphics operations, as operation name and fully qualified class name
    private static IEnumerable<OperationAndTypeName> GetGraphicOperations(Compilation compilation)
    {
        // Find any classes whose base types includes IGraphicsStateOperation
        IEnumerable<SyntaxNode> allNodes = compilation.SyntaxTrees.SelectMany(s => s.GetRoot().DescendantNodes());
        IEnumerable<ClassDeclarationSyntax> allClasses = allNodes
            .Where(d => d.IsKind(SyntaxKind.ClassDeclaration))
            .OfType<ClassDeclarationSyntax>()
            .Where(cls => cls.BaseList?.Types.Any(baseType => baseType.ToString() == OperationInterfaceName) == true);

        // Try to get the symbol and class type for this operation
        return allClasses
            .Select(component => GetGraphicOperationSymbol(compilation, component))
            .Where(operation => operation is not null)
            .Cast<OperationAndTypeName>();
    }

    // From https://andrewlock.net/creating-a-source-generator-part-5-finding-a-type-declarations-namespace-and-type-hierarchy/#finding-the-namespace-for-a-class-syntax
    // We want to fully qualify the names of our operation class references, so we don't need to worry about generating all the matching using declarations.
    private static string GetNamespace(BaseTypeDeclarationSyntax syntax)
    {
        string nameSpace = string.Empty;

        var namespaceParent = syntax.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();

        if (namespaceParent is null)
        {
            return nameSpace;
        }

        nameSpace = namespaceParent.Name.ToString();

        while ((namespaceParent = namespaceParent!.Parent?.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()) is not null)
        {
            nameSpace = $"{namespaceParent.Name}.{nameSpace}";
        }

        return nameSpace;
    }

    // Small holder type for an operation name / class type
    private sealed record OperationAndTypeName(string Operation, string TypeName);

    // Try to get the symbol field and class type for this operation.
    private static OperationAndTypeName? GetGraphicOperationSymbol(Compilation compilation, ClassDeclarationSyntax component)
    {
        // Try to extract the Symbol field from the class definition
        var symbolField = component.Members
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables)
                .FirstOrDefault(variable => variable.Identifier.ValueText == "Symbol");

        if (symbolField is not null)
        {
            if (symbolField.Initializer?.Value is LiteralExpressionSyntax literal)
            {
                var typeName = $"{GetNamespace(component)}.{component.Identifier.Text}";
                return new OperationAndTypeName(literal.Token.ValueText, typeName);
            }
        }

        return null;
    }
}
