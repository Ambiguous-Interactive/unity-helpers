// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    internal static partial class ClosureScan
    {
        private sealed class ScanResult
        {
            internal readonly List<TypeUse> Uses = new List<TypeUse>();
            internal readonly List<MethodUse> Calls = new List<MethodUse>();
            internal readonly Dictionary<IMethodSymbol, SerializationRequirements> Requirements =
                new Dictionary<IMethodSymbol, SerializationRequirements>(
                    SymbolEqualityComparer.Default
                );
            internal readonly List<IArrayTypeSymbol> Arrays;

            internal ScanResult(List<IArrayTypeSymbol> arrays)
            {
                Arrays = arrays;
            }
        }
    }
}
