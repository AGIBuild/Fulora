using System.Collections.Immutable;
using System.Linq;
using Agibuild.Fulora.Bridge.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Tests for the Bridge Contract IR: DTO discovery, type reference building, and IR aggregation.
/// </summary>
public sealed class ContractIrTests
{
    // ==================== DTO discovery via source generator ====================

    [Fact]
    public void DTO_types_are_discovered_from_method_parameters()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record UserProfile(string Name, int Age);

[JsExport]
public interface ITestService
{
    Task SaveUser(UserProfile user);
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);

        var dto = model.ReferencedDtos[0];
        Assert.Equal("TestNs.UserProfile", dto.FullName);
        Assert.Equal("UserProfile", dto.Name);
        Assert.False(dto.IsEnum);
        Assert.Equal(2, dto.Properties.Length);

        var nameProp = dto.Properties.First(p => p.Name == "Name");
        Assert.Equal("name", nameProp.CamelCaseName);
        Assert.Equal(BridgeTypeKind.String, nameProp.TypeRef.Kind);

        var ageProp = dto.Properties.First(p => p.Name == "Age");
        Assert.Equal("age", ageProp.CamelCaseName);
        Assert.Equal(BridgeTypeKind.Number, ageProp.TypeRef.Kind);
    }

    [Fact]
    public void DTO_types_are_discovered_from_return_types()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record Item(int Id, string Title);

[JsExport]
public interface ITestService
{
    Task<Item> GetItem();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);
        Assert.Equal("TestNs.Item", model.ReferencedDtos[0].FullName);
    }

    [Fact]
    public void DTO_types_are_discovered_from_collection_element_types()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record Item(int Id, string Title);

[JsExport]
public interface ITestService
{
    Task<List<Item>> GetItems();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);
        Assert.Equal("TestNs.Item", model.ReferencedDtos[0].FullName);
    }

    [Fact]
    public void Nested_DTO_types_are_discovered_recursively()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record Address(string Street, string City);
public record UserProfile(string Name, Address HomeAddress);

[JsExport]
public interface ITestService
{
    Task<UserProfile> GetUser();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Equal(2, model!.ReferencedDtos.Length);

        var names = model.ReferencedDtos.Select(d => d.Name).OrderBy(n => n).ToArray();
        Assert.Equal("Address", names[0]);
        Assert.Equal("UserProfile", names[1]);

        var userDto = model.ReferencedDtos.First(d => d.Name == "UserProfile");
        var addressProp = userDto.Properties.First(p => p.Name == "HomeAddress");
        Assert.Equal(BridgeTypeKind.Dto, addressProp.TypeRef.Kind);
        Assert.Equal("TestNs.Address", addressProp.TypeRef.FullName);
    }

    [Fact]
    public void Enum_types_are_discovered_with_members()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public enum Status { Active, Inactive, Pending }

[JsExport]
public interface ITestService
{
    Task<Status> GetStatus();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);

        var dto = model.ReferencedDtos[0];
        Assert.Equal("TestNs.Status", dto.FullName);
        Assert.True(dto.IsEnum);
        Assert.Equal(3, dto.EnumMembers.Length);

        Assert.Equal("Active", dto.EnumMembers[0].Name);
        Assert.Equal("active", dto.EnumMembers[0].CamelCaseName);
        Assert.Equal("0", dto.EnumMembers[0].ValueLiteral);

        Assert.Equal("Inactive", dto.EnumMembers[1].Name);
        Assert.Equal("1", dto.EnumMembers[1].ValueLiteral);

        Assert.Equal("Pending", dto.EnumMembers[2].Name);
        Assert.Equal("2", dto.EnumMembers[2].ValueLiteral);
    }

    [Fact]
    public void Primitive_types_are_not_discovered_as_DTOs()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

[JsExport]
public interface ITestService
{
    Task<string> GetName(int id, bool active, double score);
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Empty(model!.ReferencedDtos);
    }

    [Fact]
    public void Dictionary_value_types_are_discovered()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record Config(string Key, string Value);

[JsExport]
public interface ITestService
{
    Task<Dictionary<string, Config>> GetConfigs();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);
        Assert.Equal("TestNs.Config", model.ReferencedDtos[0].FullName);
    }

    [Fact]
    public void Circular_DTO_references_do_not_cause_infinite_recursion()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public class TreeNode
{
    public string Label { get; set; } = """";
    public TreeNode? Parent { get; set; }
}

[JsExport]
public interface ITestService
{
    Task<TreeNode> GetRoot();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);

        var treeDto = model.ReferencedDtos[0];
        Assert.Equal("TestNs.TreeNode", treeDto.FullName);
        Assert.Equal(2, treeDto.Properties.Length);

        var parentProp = treeDto.Properties.First(p => p.Name == "Parent");
        Assert.Equal(BridgeTypeKind.Dto, parentProp.TypeRef.Kind);
        Assert.True(parentProp.IsNullable);
    }

    [Fact]
    public void Inherited_DTO_properties_are_captured()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public class BaseEntity
{
    public int Id { get; set; }
    public string CreatedAt { get; set; } = """";
}

public class UserEntity : BaseEntity
{
    public string UserName { get; set; } = """";
}

[JsExport]
public interface ITestService
{
    Task<UserEntity> GetUser();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Single(model!.ReferencedDtos);

        var dto = model.ReferencedDtos[0];
        Assert.Equal("TestNs.UserEntity", dto.FullName);
        Assert.Equal(3, dto.Properties.Length);
        Assert.Contains(dto.Properties, p => p.Name == "Id");
        Assert.Contains(dto.Properties, p => p.Name == "CreatedAt");
        Assert.Contains(dto.Properties, p => p.Name == "UserName");
    }

    [Fact]
    public void CancellationToken_parameters_do_not_produce_DTOs()
    {
        var source = @"
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

[JsExport]
public interface ITestService
{
    Task DoWork(CancellationToken ct);
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);
        Assert.Empty(model!.ReferencedDtos);
    }

    // ==================== Type reference building ====================

    [Fact]
    public void TypeRef_for_collection_of_DTOs_has_correct_structure()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record Item(int Id, string Title);

[JsExport]
public interface ITestService
{
    Task<List<Item>> GetItems();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);

        var dto = model!.ReferencedDtos.First(d => d.Name == "Item");
        var idProp = dto.Properties.First(p => p.Name == "Id");
        Assert.Equal(BridgeTypeKind.Number, idProp.TypeRef.Kind);

        var titleProp = dto.Properties.First(p => p.Name == "Title");
        Assert.Equal(BridgeTypeKind.String, titleProp.TypeRef.Kind);
    }

    [Fact]
    public void TypeRef_for_nullable_property_is_marked()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

#nullable enable

namespace TestNs;

public class Item
{
    public string? Description { get; set; }
    public int? Count { get; set; }
}

[JsExport]
public interface ITestService
{
    Task<Item> GetItem();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);

        var dto = model!.ReferencedDtos.First(d => d.Name == "Item");

        var descProp = dto.Properties.First(p => p.Name == "Description");
        Assert.True(descProp.IsNullable);

        var countProp = dto.Properties.First(p => p.Name == "Count");
        Assert.True(countProp.IsNullable || countProp.TypeRef.IsNullable);
    }

    [Fact]
    public void TypeRef_for_DateTime_maps_to_DateTime_kind()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public class Event
{
    public DateTime StartDate { get; set; }
    public Guid EventId { get; set; }
}

[JsExport]
public interface ITestService
{
    Task<Event> GetEvent();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);

        var dto = model!.ReferencedDtos.First(d => d.Name == "Event");

        var dateProp = dto.Properties.First(p => p.Name == "StartDate");
        Assert.Equal(BridgeTypeKind.DateTime, dateProp.TypeRef.Kind);

        var guidProp = dto.Properties.First(p => p.Name == "EventId");
        Assert.Equal(BridgeTypeKind.Guid, guidProp.TypeRef.Kind);
    }

    [Fact]
    public void TypeRef_for_byte_array_maps_to_Binary_kind()
    {
        var source = @"
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public class FileData
{
    public byte[] Content { get; set; } = System.Array.Empty<byte>();
}

[JsExport]
public interface ITestService
{
    Task<FileData> GetFile();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);

        var dto = model!.ReferencedDtos.First(d => d.Name == "FileData");
        var contentProp = dto.Properties.First(p => p.Name == "Content");
        Assert.Equal(BridgeTypeKind.Binary, contentProp.TypeRef.Kind);
    }

    [Fact]
    public void TypeRef_for_dictionary_property_has_key_value_args()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public class Options
{
    public Dictionary<string, int> Settings { get; set; } = new();
}

[JsExport]
public interface ITestService
{
    Task<Options> GetOptions();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);

        var dto = model!.ReferencedDtos.First(d => d.Name == "Options");
        var settingsProp = dto.Properties.First(p => p.Name == "Settings");
        Assert.Equal(BridgeTypeKind.Dictionary, settingsProp.TypeRef.Kind);
        Assert.Equal(2, settingsProp.TypeRef.TypeArguments.Length);
        Assert.Equal(BridgeTypeKind.String, settingsProp.TypeRef.TypeArguments[0].Kind);
        Assert.Equal(BridgeTypeKind.Number, settingsProp.TypeRef.TypeArguments[1].Kind);
    }

    [Fact]
    public void TypeRef_for_list_property_has_element_type()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace TestNs;

public record Tag(string Label);

public class Article
{
    public List<Tag> Tags { get; set; } = new();
}

[JsExport]
public interface ITestService
{
    Task<Article> GetArticle();
}
";
        var model = RunGeneratorAndGetModel(source);
        Assert.NotNull(model);

        var articleDto = model!.ReferencedDtos.First(d => d.Name == "Article");
        var tagsProp = articleDto.Properties.First(p => p.Name == "Tags");
        Assert.Equal(BridgeTypeKind.Array, tagsProp.TypeRef.Kind);
        Assert.NotNull(tagsProp.TypeRef.ElementType);
        Assert.Equal(BridgeTypeKind.Dto, tagsProp.TypeRef.ElementType!.Kind);
        Assert.Equal("TestNs.Tag", tagsProp.TypeRef.ElementType.FullName);
    }

    // ==================== Contract IR builder ====================

    [Fact]
    public void ContractIrBuilder_deduplicates_DTOs_across_services()
    {
        var sharedDto = new BridgeDtoModel
        {
            FullName = "TestNs.SharedDto",
            Name = "SharedDto",
        };

        var export = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IServiceA",
            ServiceName = "ServiceA",
            Direction = BridgeDirection.Export,
            ReferencedDtos = ImmutableArray.Create(sharedDto),
        };

        var import = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IServiceB",
            ServiceName = "ServiceB",
            Direction = BridgeDirection.Import,
            ReferencedDtos = ImmutableArray.Create(sharedDto),
        };

        var ir = ContractIrBuilder.Build(
            ImmutableArray.Create(export),
            ImmutableArray.Create(import));

        Assert.Equal(2, ir.Services.Length);
        Assert.Single(ir.Dtos);
        Assert.Equal("TestNs.SharedDto", ir.Dtos[0].FullName);
    }

    [Fact]
    public void ContractIrBuilder_excludes_invalid_models()
    {
        var invalid = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IBadService",
            ServiceName = "BadService",
            Direction = BridgeDirection.Export,
            ValidationErrors = ImmutableArray.Create(new BridgeDiagnosticInfo("AGBR001", "SomeMethod")),
        };

        var valid = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IGoodService",
            ServiceName = "GoodService",
            Direction = BridgeDirection.Export,
        };

        var ir = ContractIrBuilder.Build(
            ImmutableArray.Create(invalid, valid),
            ImmutableArray<BridgeInterfaceModel>.Empty);

        Assert.Single(ir.Services);
        Assert.Equal("GoodService", ir.Services[0].ServiceName);
    }

    [Fact]
    public void ContractIrBuilder_combines_exports_and_imports()
    {
        var export = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IExportSvc",
            ServiceName = "ExportSvc",
            Direction = BridgeDirection.Export,
        };

        var import = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IImportSvc",
            ServiceName = "ImportSvc",
            Direction = BridgeDirection.Import,
        };

        var ir = ContractIrBuilder.Build(
            ImmutableArray.Create(export),
            ImmutableArray.Create(import));

        Assert.Equal(2, ir.Services.Length);
        Assert.Contains(ir.Services, s => s.Direction == BridgeDirection.Export);
        Assert.Contains(ir.Services, s => s.Direction == BridgeDirection.Import);
    }

    // ==================== Deterministic ordering ====================

    [Fact]
    public void ContractIrBuilder_orders_services_by_direction_then_name()
    {
        var importB = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IImportB",
            ServiceName = "ImportB",
            Direction = BridgeDirection.Import,
        };
        var exportA = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IExportA",
            ServiceName = "ExportA",
            Direction = BridgeDirection.Export,
        };
        var exportC = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "IExportC",
            ServiceName = "ExportC",
            Direction = BridgeDirection.Export,
        };

        var ir = ContractIrBuilder.Build(
            ImmutableArray.Create(exportC, exportA),
            ImmutableArray.Create(importB));

        Assert.Equal(3, ir.Services.Length);
        Assert.Equal("ExportA", ir.Services[0].ServiceName);
        Assert.Equal("ExportC", ir.Services[1].ServiceName);
        Assert.Equal("ImportB", ir.Services[2].ServiceName);
    }

    [Fact]
    public void ContractIrBuilder_orders_DTOs_by_full_name()
    {
        var dtoZ = new BridgeDtoModel { FullName = "Z.Zebra", Name = "Zebra" };
        var dtoA = new BridgeDtoModel { FullName = "A.Apple", Name = "Apple" };
        var dtoM = new BridgeDtoModel { FullName = "M.Mango", Name = "Mango" };

        var export = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "ISvc",
            ServiceName = "Svc",
            Direction = BridgeDirection.Export,
            ReferencedDtos = ImmutableArray.Create(dtoZ, dtoA, dtoM),
        };

        var ir = ContractIrBuilder.Build(
            ImmutableArray.Create(export),
            ImmutableArray<BridgeInterfaceModel>.Empty);

        Assert.Equal(3, ir.Dtos.Length);
        Assert.Equal("A.Apple", ir.Dtos[0].FullName);
        Assert.Equal("M.Mango", ir.Dtos[1].FullName);
        Assert.Equal("Z.Zebra", ir.Dtos[2].FullName);
    }

    [Fact]
    public void ContractIrBuilder_produces_identical_output_on_repeated_builds()
    {
        var dto = new BridgeDtoModel { FullName = "TestNs.Dto", Name = "Dto" };
        var svc = new BridgeInterfaceModel
        {
            Namespace = "TestNs",
            InterfaceName = "ISvc",
            ServiceName = "Svc",
            Direction = BridgeDirection.Export,
            ReferencedDtos = ImmutableArray.Create(dto),
        };

        var exports = ImmutableArray.Create(svc);
        var imports = ImmutableArray<BridgeInterfaceModel>.Empty;

        var ir1 = ContractIrBuilder.Build(exports, imports);
        var ir2 = ContractIrBuilder.Build(exports, imports);

        Assert.Equal(ir1.Services.Length, ir2.Services.Length);
        Assert.Equal(ir1.Dtos.Length, ir2.Dtos.Length);

        for (int i = 0; i < ir1.Services.Length; i++)
            Assert.Equal(ir1.Services[i].ServiceName, ir2.Services[i].ServiceName);

        for (int i = 0; i < ir1.Dtos.Length; i++)
            Assert.Equal(ir1.Dtos[i].FullName, ir2.Dtos[i].FullName);
    }

    // ==================== Helper ====================

    private static BridgeInterfaceModel? RunGeneratorAndGetModel(string source)
    {
        var coreAssemblyPath = typeof(JsExportAttribute).Assembly.Location;
        var runtimeAssemblyDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(coreAssemblyPath),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeAssemblyDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeAssemblyDir, "System.Private.CoreLib.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeAssemblyDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeAssemblyDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new WebViewBridgeGenerator();
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        var model = compilation.GetTypeByMetadataName("TestNs.ITestService");
        if (model == null) return null;

        var attribute = model.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Agibuild.Fulora.JsExportAttribute"
                              || a.AttributeClass?.ToDisplayString() == "Agibuild.Fulora.JsImportAttribute");
        if (attribute == null) return null;

        var direction = attribute.AttributeClass!.Name.Contains("Export")
            ? BridgeDirection.Export
            : BridgeDirection.Import;

        return ModelExtractor.Extract(model, attribute, direction);
    }
}
