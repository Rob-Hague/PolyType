﻿using PolyType.Roslyn;
using PolyType.SourceGenerator.Model;

namespace PolyType.SourceGenerator;

internal static partial class SourceFormatter
{
    private static void FormatNullableTypeShapeFactory(SourceWriter writer, string methodName, NullableShapeModel nullableShapeModel)
    {
        writer.WriteLine($$"""
            private global::PolyType.Abstractions.ITypeShape<{{nullableShapeModel.Type.FullyQualifiedName}}> {{methodName}}()
            {
                return new global::PolyType.SourceGenModel.SourceGenNullableTypeShape<{{nullableShapeModel.ElementType.FullyQualifiedName}}>
                {
                    Provider = this,
                    ElementType = {{nullableShapeModel.ElementType.GeneratedPropertyName}},
                };
            }
            """);
    }
}
