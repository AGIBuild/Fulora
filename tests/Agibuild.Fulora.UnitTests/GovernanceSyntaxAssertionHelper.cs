using System.Text.RegularExpressions;
using Agibuild.Fulora.Testing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Agibuild.Fulora.UnitTests;

internal static class GovernanceSyntaxAssertionHelper
{
    public static void AssertTargetDeclarationExists(
        string source,
        string targetName,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Any(property =>
                string.Equals(property.Type.ToString(), "Target", StringComparison.Ordinal)
                && string.Equals(property.Identifier.ValueText, targetName, StringComparison.Ordinal));

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"target declaration 'Target {targetName} => _ => _' exists",
                "not found");
        }
    }

    public static void AssertTargetDependsOnContainsAll(
        string source,
        string targetName,
        IEnumerable<string> requiredDependencies,
        string invariantId,
        string artifactPath)
    {
        var declaredDependencies = ReadTargetDependsOnDependencies(source, targetName, invariantId, artifactPath);

        var missing = requiredDependencies
            .Where(required => !declaredDependencies.Contains(required))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"target '{targetName}' DependsOn includes [{string.Join(", ", requiredDependencies)}]",
                $"missing dependencies: [{string.Join(", ", missing)}]");
        }
    }

    public static IReadOnlySet<string> ReadTargetDependsOnDependencies(
        string source,
        string targetName,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var targetProperty = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(property =>
                string.Equals(property.Type.ToString(), "Target", StringComparison.Ordinal)
                && string.Equals(property.Identifier.ValueText, targetName, StringComparison.Ordinal));

        if (targetProperty is null)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"target declaration '{targetName}' exists",
                "target missing");
        }

        var expression = targetProperty.ExpressionBody?.Expression;
        if (expression is null)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"target '{targetName}' has expression body",
                "expression body missing");
        }

        var dependsOnInvocation = expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && string.Equals(memberAccess.Name.Identifier.ValueText, "DependsOn", StringComparison.Ordinal));

        if (dependsOnInvocation is null)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"target '{targetName}' contains DependsOn(...)",
                "DependsOn invocation missing");
        }

        return dependsOnInvocation.ArgumentList.Arguments
            .Select(argument => argument.Expression.ToString())
            .ToHashSet(StringComparer.Ordinal);
    }

    public static void AssertAssignmentValueIn(
        string source,
        string leftExpression,
        IEnumerable<string> acceptedRightExpressions,
        string invariantId,
        string artifactPath)
    {
        static string Normalize(string value) => Regex.Replace(value, @"\s+", string.Empty);

        var normalizedLeft = Normalize(leftExpression);
        var normalizedAccepted = acceptedRightExpressions
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);
        var isSimpleIdentifier = Regex.IsMatch(leftExpression, @"^[A-Za-z_][A-Za-z0-9_]*$");

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var assignmentMatch = root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
            {
                var left = Normalize(assignment.Left.ToString());
                var right = Normalize(assignment.Right.ToString());
                return string.Equals(left, normalizedLeft, StringComparison.Ordinal)
                    && normalizedAccepted.Contains(right);
            });
        var variableInitializerMatch = isSimpleIdentifier && root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable =>
                string.Equals(variable.Identifier.ValueText, leftExpression, StringComparison.Ordinal)
                && variable.Initializer is not null
                && normalizedAccepted.Contains(Normalize(variable.Initializer.Value.ToString())));
        var anonymousMemberMatch = isSimpleIdentifier && root.DescendantNodes()
            .OfType<AnonymousObjectMemberDeclaratorSyntax>()
            .Any(member =>
                member.NameEquals is not null
                && string.Equals(member.NameEquals.Name.Identifier.ValueText, leftExpression, StringComparison.Ordinal)
                && normalizedAccepted.Contains(Normalize(member.Expression.ToString())));
        var found = assignmentMatch || variableInitializerMatch || anonymousMemberMatch;

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"assignment '{leftExpression} = <value>' with value in [{string.Join(", ", normalizedAccepted)}]",
                "not found");
        }
    }

    public static void AssertAnonymousObjectHasMembers(
        string source,
        IEnumerable<string> requiredMemberNames,
        string invariantId,
        string artifactPath)
    {
        var required = requiredMemberNames
            .ToHashSet(StringComparer.Ordinal);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var declared = root.DescendantNodes()
            .OfType<AnonymousObjectMemberDeclaratorSyntax>()
            .Select(GetAnonymousMemberName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        var missing = required.Where(name => !declared.Contains(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"anonymous object members include [{string.Join(", ", required)}]",
                $"missing members: [{string.Join(", ", missing)}]");
        }
    }

    public static void AssertAnonymousObjectMemberAssignedWithNew(
        string source,
        string memberName,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<AnonymousObjectMemberDeclaratorSyntax>()
            .Any(member =>
            {
                if (!string.Equals(GetAnonymousMemberName(member), memberName, StringComparison.Ordinal))
                    return false;

                return member.Expression is AnonymousObjectCreationExpressionSyntax
                    || member.Expression is ObjectCreationExpressionSyntax;
            });

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"anonymous member '{memberName}' is assigned with new object expression",
                "not found");
        }
    }

    public static void AssertIndexerStringKeyAssignmentExists(
        string source,
        string key,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
            {
                if (assignment.Left is not ElementAccessExpressionSyntax elementAccess)
                    return false;

                var arg = elementAccess.ArgumentList.Arguments.FirstOrDefault();
                if (arg?.Expression is not LiteralExpressionSyntax literal)
                    return false;

                return literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                    && string.Equals(literal.Token.ValueText, key, StringComparison.Ordinal);
            });

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"indexer assignment with key \"{key}\" exists",
                "not found");
        }
    }

    private static string? GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax member)
    {
        if (member.NameEquals is not null)
            return member.NameEquals.Name.Identifier.ValueText;

        return member.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };
    }

    public static void AssertMemberInvocationExists(
        string source,
        string containingMemberName,
        string invokedMethodName,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    return false;

                if (!string.Equals(memberAccess.Name.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal))
                    return false;

                return memberAccess.Expression.ToString().Contains(containingMemberName, StringComparison.Ordinal);
            });

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"member invocation '*.{containingMemberName}*.{invokedMethodName}(...)' exists",
                "not found");
        }
    }

    public static void AssertStringLiteralExists(
        string source,
        string expectedValue,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantTokens()
            .Any(token => token.RawKind == (int)SyntaxKind.StringLiteralToken
                && string.Equals(token.ValueText, expectedValue, StringComparison.Ordinal));

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"string literal '{expectedValue}' exists",
                "not found");
        }
    }

    public static void AssertInvocationFirstArgumentIn(
        string source,
        string invokedMethodName,
        IReadOnlySet<string> acceptedArguments,
        string invariantId,
        string artifactPath)
    {
        var normalizedAccepted = acceptedArguments
            .Select(arg => Regex.Replace(arg, @"\s+", string.Empty))
            .ToHashSet(StringComparer.Ordinal);

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                if (invocation.Expression is not SimpleNameSyntax simpleName)
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                        return false;

                    if (!string.Equals(memberAccess.Name.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal))
                        return false;
                }
                else if (!string.Equals(simpleName.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal))
                {
                    return false;
                }

                var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
                if (firstArg is null)
                    return false;

                var normalized = Regex.Replace(firstArg.Expression.ToString(), @"\s+", string.Empty);
                return normalizedAccepted.Contains(normalized);
            });

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"{invokedMethodName}(...) with first argument in [{string.Join(", ", normalizedAccepted)}]",
                "not found");
        }
    }

    public static void AssertInvocationContainsStringArgument(
        string source,
        string invokedMethodName,
        string expectedStringArgument,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                var methodNameMatches = invocation.Expression switch
                {
                    SimpleNameSyntax simpleName => string.Equals(simpleName.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal),
                    MemberAccessExpressionSyntax memberAccess => string.Equals(memberAccess.Name.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal),
                    _ => false
                };
                if (!methodNameMatches)
                    return false;

                return invocation.ArgumentList.Arguments
                    .Select(argument => argument.Expression)
                    .OfType<LiteralExpressionSyntax>()
                    .Any(literal =>
                        literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                        && string.Equals(literal.Token.ValueText, expectedStringArgument, StringComparison.Ordinal));
            });

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"{invokedMethodName}(...) contains string argument \"{expectedStringArgument}\"",
                "not found");
        }
    }

    public static void AssertInvocationExists(
        string source,
        string invokedMethodName,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                return invocation.Expression switch
                {
                    SimpleNameSyntax simpleName => string.Equals(simpleName.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal),
                    MemberAccessExpressionSyntax memberAccess => string.Equals(memberAccess.Name.Identifier.ValueText, invokedMethodName, StringComparison.Ordinal),
                    _ => false
                };
            });

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"invocation '{invokedMethodName}(...)' exists",
                "not found");
        }
    }

    public static void AssertStringLiteralContains(
        string source,
        string expectedSubstring,
        string invariantId,
        string artifactPath)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var found = root.DescendantTokens()
            .Any(token => token.RawKind == (int)SyntaxKind.StringLiteralToken
                && token.ValueText.Contains(expectedSubstring, StringComparison.Ordinal));

        if (!found)
        {
            throw new GovernanceInvariantViolationException(
                invariantId,
                artifactPath,
                $"string literal contains '{expectedSubstring}'",
                "not found");
        }
    }
}
