using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.AspNetCore.SignalR.Analyzers
{
    internal class SignalRAnalysisContext
    {
        public CompilationStartAnalysisContext Context { get; }

        private INamedTypeSymbol _hubClass;
        public INamedTypeSymbol HubClass => GetType(TypeNames.Hub, ref _hubClass);

        public SignalRAnalysisContext(CompilationStartAnalysisContext compilationContext)
        {
            Context = compilationContext ?? throw new System.ArgumentNullException(nameof(compilationContext));
        }

        private INamedTypeSymbol GetType(string name, ref INamedTypeSymbol cache) =>
            cache = cache ?? Context.Compilation.GetTypeByMetadataName(name);
    }
}
