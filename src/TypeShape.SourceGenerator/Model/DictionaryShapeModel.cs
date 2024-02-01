﻿using TypeShape.Roslyn;

namespace TypeShape.SourceGenerator.Model;

public sealed record DictionaryShapeModel : TypeShapeModel
{
    public required TypeId KeyType { get; init; }
    public required TypeId ValueType { get; init; }
    public required DictionaryKind Kind { get; init; }
    public required CollectionConstructionStrategy ConstructionStrategy { get; init; }
    public required string? ImplementationTypeFQN { get; init; }
    public required string? EnumerableFactoryMethod { get; init; }
    public required string? SpanFactoryMethod { get; init; }
}