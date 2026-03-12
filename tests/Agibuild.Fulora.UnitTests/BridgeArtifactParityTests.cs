using System.Collections.Immutable;
using Agibuild.Fulora.Bridge.Generator;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class BridgeArtifactParityTests
{
    [Fact]
    public void Declaration_client_and_mock_keep_method_identity_and_shape_aligned()
    {
        var stringRef = new BridgeTypeRef
        {
            Kind = BridgeTypeKind.String,
            FullName = "System.String",
            Name = "string"
        };
        var numberRef = new BridgeTypeRef
        {
            Kind = BridgeTypeKind.Number,
            FullName = "System.Int32",
            Name = "int"
        };

        var service = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IDocService",
            ServiceName = "DocService",
            Direction = BridgeDirection.Export,
            Methods =
            [
                new BridgeMethodModel
                {
                    Name = "Load",
                    CamelCaseName = "load",
                    RpcMethodName = "DocService.load",
                    ReturnTypeFullName = "System.Threading.Tasks.Task<System.String>",
                    ReturnTypeRef = stringRef,
                    IsAsync = true,
                    HasReturnValue = true,
                    InnerReturnTypeFullName = "System.String",
                    InnerReturnTypeRef = stringRef,
                    Parameters =
                    [
                        new BridgeParameterModel
                        {
                            Name = "query",
                            CamelCaseName = "query",
                            TypeFullName = "System.String",
                            TypeRef = stringRef
                        },
                        new BridgeParameterModel
                        {
                            Name = "ct",
                            CamelCaseName = "ct",
                            TypeFullName = "System.Threading.CancellationToken",
                            TypeRef = BridgeTypeRef.UnknownRef,
                            IsCancellationToken = true
                        }
                    ]
                },
                new BridgeMethodModel
                {
                    Name = "Stream",
                    CamelCaseName = "stream",
                    RpcMethodName = "DocService.stream",
                    ReturnTypeFullName = "System.Collections.Generic.IAsyncEnumerable<System.Int32>",
                    ReturnTypeRef = new BridgeTypeRef
                    {
                        Kind = BridgeTypeKind.AsyncEnumerable,
                        FullName = "System.Collections.Generic.IAsyncEnumerable<System.Int32>",
                        Name = "IAsyncEnumerable",
                        ElementType = numberRef
                    },
                    IsAsyncEnumerable = true,
                    HasReturnValue = true,
                    InnerReturnTypeFullName = "System.Int32",
                    InnerReturnTypeRef = numberRef,
                    AsyncEnumerableInnerType = "System.Int32",
                    AsyncEnumerableInnerTypeRef = numberRef
                }
            ]
        };

        var ir = new BridgeContractModel
        {
            Services = ImmutableArray.Create(service),
            Dtos = ImmutableArray<BridgeDtoModel>.Empty
        };

        var declarations = TypeScriptEmitter.EmitDeclarations(ir);
        var client = TypeScriptClientEmitter.EmitClient(ir);
        var mock = TypeScriptMockEmitter.EmitMock(ir);

        Assert.Contains("load(query: string, options?: { signal?: AbortSignal }): Promise<string>;", declarations);
        Assert.Contains("stream(): AsyncIterable<number>;", declarations);

        Assert.Contains("return rpc().invoke('DocService.load', { query }, options && options.signal)", client);
        Assert.Contains("return rpc()._createAsyncIterable('DocService.stream', undefined) as AsyncIterable<number>;", client);

        Assert.Contains("bridge['docService']", mock);
        Assert.Contains("load: (..._args: unknown[]) => Promise.resolve", mock);
        Assert.Contains("stream: (..._args: unknown[]) => Promise.resolve", mock);
    }
}
