﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using TypeShape.SourceGenerator.Helpers;
using TypeShape.SourceGenerator.Model;

namespace TypeShape.SourceGenerator;

public sealed partial class ModelGenerator
{
    private const string GlobalNamespacePrefix = "global::";
    private const string GenerateShapeAttributeFQN = "global::TypeShape.GenerateShapeAttribute";
    private const string GlobalNamespaceIdentifier = "<global namespace>";

    private readonly SemanticModel _semanticModel;
    private readonly CancellationToken _cancellationToken;
    private readonly ClassDeclarationSyntax _classDeclarationSyntax;

    private readonly Dictionary<ITypeSymbol, TypeModel> _generatedTypes = new(SymbolEqualityComparer.Default);
    private readonly Queue<ITypeSymbol> _typesToGenerate = new();
    private readonly List<Diagnostic> _diagnostics = new();

    private readonly ITypeSymbol? _iReadOnlyDictionaryOfTKeyTValue;
    private readonly ITypeSymbol? _iDictionaryOfTKeyTValue;
    private readonly ITypeSymbol? _iDictionary;
    private readonly ITypeSymbol? _iList;

    public ModelGenerator(ClassDeclarationSyntax classDeclarationSyntax, Compilation compilation, CancellationToken cancellationToken)
    {
        _classDeclarationSyntax = classDeclarationSyntax;
        _cancellationToken = cancellationToken;
        _semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);

        _iReadOnlyDictionaryOfTKeyTValue = _semanticModel.Compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        _iDictionaryOfTKeyTValue = _semanticModel.Compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        _iDictionary = _semanticModel.Compilation.GetTypeByMetadataName("System.Collections.IDictionary");
        _iList = _semanticModel.Compilation.GetTypeByMetadataName("System.Collections.IList");
    }

    public static TypeShapeProviderModel Compile(ClassDeclarationSyntax classDeclarationSyntax, Compilation compilation, CancellationToken cancellationToken)
    {
        ModelGenerator compiler = new(classDeclarationSyntax, compilation, cancellationToken);
        return compiler.Compile();
    }

    public TypeShapeProviderModel Compile()
    {
        INamedTypeSymbol? typeSymbol = _semanticModel.GetDeclaredSymbol(_classDeclarationSyntax, _cancellationToken) as INamedTypeSymbol;
        Debug.Assert(typeSymbol != null);

        ReadConfigurationFromAttributes();
        TraverseTypeGraph();

        return new TypeShapeProviderModel
        {
            Name = typeSymbol!.Name,
            Namespace = FormatNamespace(typeSymbol),
            ProvidedTypes = _generatedTypes.Values.OrderBy(type => type.Id.FullyQualifiedName).ToImmutableArrayEq(),
            TypeDeclaration = ResolveDeclarationHeader(_classDeclarationSyntax, out ImmutableArrayEq<string>? containingTypes),
            ContainingTypes = containingTypes,
            Diagnostics = _diagnostics.ToImmutableArrayEq(),
        };
    }

    private void TraverseTypeGraph()
    {
        while (_typesToGenerate.Count > 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            ITypeSymbol type = _typesToGenerate.Dequeue();
            if (!_generatedTypes.ContainsKey(type))
            {
                TypeModel generatedType = MapType(type);
                _generatedTypes.Add(type, generatedType);
            }
        }
    }

    private TypeModel MapType(ITypeSymbol type)
    {
        TypeId typeId = CreateTypeId(type);

        bool isSpecialTypeKind = TryResolveSpecialTypeKinds(typeId, type,
            out EnumTypeModel? enumType,
            out NullableTypeModel? nullableType, 
            out DictionaryTypeModel? dictionaryType, 
            out EnumerableTypeModel? enumerableType, 
            out ITypeSymbol? implementedCollectionType);

        return new TypeModel
        {
            Id = typeId,
            Properties = MapProperties(typeId, type, isSpecialTypeKind),
            Constructors = MapConstructors(typeId, type, implementedCollectionType),
            EnumType = enumType,
            NullableType = nullableType,
            EnumerableType = enumerableType,
            DictionaryType = dictionaryType,
        };
    }

    private void ReadConfigurationFromAttributes()
    {
        foreach (AttributeSyntax attributeSyntax in _classDeclarationSyntax.AttributeLists.SelectMany(attrList => attrList.Attributes))
        {
            if (_semanticModel.GetSymbolInfo(attributeSyntax, _cancellationToken).Symbol is not IMethodSymbol attributeSymbol)
            {
                continue;
            }

            INamedTypeSymbol attributeTypeSymbol = attributeSymbol.ContainingType;
            if (attributeTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == GenerateShapeAttributeFQN &&
                ReadTypeSymbolFromGenerateShapeAttribute(attributeSyntax) is ITypeSymbol typeSymbol)
            {
                if (!IsSupportedType(typeSymbol))
                {
                    ReportDiagnostic(
                        Diagnostic.Create(TypeNotSupported, attributeSyntax.GetLocation(), typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

                    continue;
                }

                _typesToGenerate.Enqueue(typeSymbol);
            }
        }
    }

    private ITypeSymbol? ReadTypeSymbolFromGenerateShapeAttribute(AttributeSyntax attributeSyntax)
    {
        foreach (SyntaxNode syntaxNode in attributeSyntax.DescendantNodes())
        {
            if (syntaxNode is AttributeArgumentSyntax &&
                syntaxNode.ChildNodes().FirstOrDefault() is TypeOfExpressionSyntax typeofExpr)
            {
                SyntaxNode typeExpr = typeofExpr.ChildNodes().Single();
                return _semanticModel.GetTypeInfo(typeExpr, _cancellationToken).ConvertedType;
            }
        }

        return null;
    }

    private TypeId GetOrCreateTypeId(ITypeSymbol type)
    {
        if (_generatedTypes.TryGetValue(type, out TypeModel? generated))
        {
            return generated.Id;
        }

        _typesToGenerate.Enqueue(type); // schedule nested type for generation
        return CreateTypeId(type);
    }

    private static TypeId CreateTypeId(ITypeSymbol type)
        => new TypeId
        {
            FullyQualifiedName = type.GetFullyQualifiedName(),
            GeneratedPropertyName = type.GetGeneratedPropertyName(),
        };

    private string ResolveDeclarationHeader(ClassDeclarationSyntax classSyntax, out ImmutableArrayEq<string> parentHeaders)
    {
        bool hierarchyNotPartial = !IsSyntaxKind(classSyntax, SyntaxKind.PartialKeyword);

        Stack<string>? parents = null;
        for (SyntaxNode? current = classSyntax.Parent; current is ClassDeclarationSyntax parent; current = current.Parent)
        {
            hierarchyNotPartial |= !IsSyntaxKind(parent, SyntaxKind.PartialKeyword);
            (parents ??= new()).Push(FormatTypeDeclarationHeader(parent));
        }

        if (hierarchyNotPartial)
        {
            ReportDiagnostic(Diagnostic.Create(ProviderTypeNotPartial, classSyntax.GetLocation(), classSyntax.Identifier));
        }

        parentHeaders = parents != null ? parentHeaders = parents.ToImmutableArrayEq() : ImmutableArrayEq<string>.Empty;
        return FormatTypeDeclarationHeader(classSyntax);

        static string FormatTypeDeclarationHeader(ClassDeclarationSyntax classSyntax)
        {
            string accessibilityModifier =
                IsSyntaxKind(classSyntax, SyntaxKind.PublicKeyword) ? "public " :
                IsSyntaxKind(classSyntax, SyntaxKind.InternalKeyword) ? "internal " :
                IsSyntaxKind(classSyntax, SyntaxKind.PrivateKeyword) ? "private " : "";

            string kindToken = classSyntax.Kind() == SyntaxKind.ClassDeclaration ? "class" : "struct";

            return $"{accessibilityModifier}partial {kindToken} {classSyntax.Identifier.ValueText}";
        }

        static bool IsSyntaxKind(ClassDeclarationSyntax classSyntax, SyntaxKind kind)
            => classSyntax.Modifiers.Any(m => m.IsKind(kind));
    }

    private static string? FormatNamespace(ITypeSymbol type)
    {
        if (type.ContainingNamespace is { } ns)
        {
            string fmt = ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fmt switch
            {
                GlobalNamespaceIdentifier => null,
                _ when (fmt.StartsWith(GlobalNamespacePrefix)) => fmt.Remove(0, GlobalNamespacePrefix.Length),
                _ => fmt
            };
        }

        return null;
    }

    private static bool IsSupportedType(ITypeSymbol typeSymbol)
        => typeSymbol.TypeKind is not (TypeKind.Pointer or TypeKind.Error) &&
          !typeSymbol.IsRefLikeType && typeSymbol.SpecialType is not SpecialType.System_Void &&
          !typeSymbol.ContainsGenericParameters();
}