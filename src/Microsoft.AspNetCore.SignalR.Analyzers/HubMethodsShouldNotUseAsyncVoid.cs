using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.AspNetCore.SignalR.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HubMethodsShouldNotUseAsyncVoid : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor DiagnosticDescriptor = new DiagnosticDescriptor(
                "SignalR1000",
                "Hub methods must not use 'async' with a 'void' return type.",
                "Hub methods must not use 'async' with a 'void' return type.",
                "Usage",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(DiagnosticDescriptor);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.RegisterCompilationStartAction(compilationContext =>
            {
                // Get the context
                var context = new SignalRAnalysisContext(compilationContext);

                context.Context.RegisterSyntaxNodeAction(c =>
                {
                    var methodSyntax = (MethodDeclarationSyntax)c.Node;
                    var method = c.SemanticModel.GetDeclaredSymbol(methodSyntax, c.CancellationToken);
                    if(!context.IsHubMethod(method))
                    {
                        return;
                    }

                    if(method.IsAsync && method.ReturnsVoid)
                    {
                        c.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptor,
                            methodSyntax.ReturnType.GetLocation()));
                    }
                }, SyntaxKind.MethodDeclaration);
            });
        }
    }
}
