using Xunit;
using OttoMapper.Mapping;
using OttoMapper.Mapping.Generated;

namespace OttoMapper.Generator.Tests;

public class GenerationTests
{
    private static IMapper Build(bool useGenerated = true)
    {
        var config = new MapperConfiguration
        {
            UseGeneratedMaps = useGenerated
        };
        return config.BuildMapper(warmUp: false);
    }

    [Fact]
    public void Registry_Contains_Generated_Simple_Map()
    {
        Assert.True(GeneratedMapRegistry.TryGet<SrcSimple, DstSimple>(out _));
    }

    [Fact]
    public void Simple_Map_Converts_And_Leaves_Unmatched_Default()
    {
        var mapper = Build();
        var src = new SrcSimple { Name = "otto", Age = 42, Id = Guid.NewGuid(), ExtraSourceOnly = "x" };

        var dst = mapper.Map<SrcSimple, DstSimple>(src);

        Assert.Equal("otto", dst.Name);
        Assert.Equal(42L, dst.Age); // int -> long conversion
        Assert.Equal(src.Id, dst.Id);
        Assert.Null(dst.DestOnly); // no matching source member
    }

    [Fact]
    public void Enum_Conversion_Works()
    {
        var mapper = Build();
        var src = new SrcWithEnum { Value = SrcEnum.B };

        var dst = mapper.Map<SrcWithEnum, DstWithEnum>(src);

        // Enum-to-enum via numeric underlying value: B=1 -> Y=1
        Assert.Equal(DstEnum.Y, dst.Value);
    }

    [Fact]
    public void Nested_And_Collection_Nested_Resolve_To_Generated_Siblings()
    {
        var mapper = Build();
        var src = new SrcParent
        {
            Child = new SrcChild { Name = "first" },
            Children = new List<SrcChild>
            {
                new SrcChild { Name = "a" },
                new SrcChild { Name = "b" },
            }
        };

        var dst = mapper.Map<SrcParent, DstParent>(src);

        Assert.NotNull(dst.Child);
        Assert.Equal("first", dst.Child.Name);
        Assert.Equal(2, dst.Children.Count);
        Assert.Equal("a", dst.Children[0].Name);
        Assert.Equal("b", dst.Children[1].Name);
    }

    [Fact]
    public void Collection_Element_Conversion_Works()
    {
        var mapper = Build();
        var src = new SrcNumbers { Values = new List<int> { 1, 2, 3 } };

        var dst = mapper.Map<SrcNumbers, DstNumbers>(src);

        Assert.Equal(new List<long> { 1L, 2L, 3L }, dst.Values);
    }

    [Fact]
    public void Array_Destination_Materializes()
    {
        var mapper = Build();
        var src = new SrcArray { Items = new List<int> { 5, 6 } };

        var dst = mapper.Map<SrcArray, DstArray>(src);

        Assert.Equal(new[] { 5, 6 }, dst.Items);
    }

    [Fact]
    public void MapSource_And_IgnoreMap_Attributes_Are_Honored()
    {
        var mapper = Build();
        var src = new SrcOverride { DisplayName = "otto", ShouldSkip = "skip-me" };

        var dst = mapper.Map<SrcOverride, DstOverride>(src);

        Assert.Equal("otto", dst.Name);   // mapped from DisplayName
        Assert.Null(dst.Skip);            // ignored
    }

    [Fact]
    public void Underscore_And_CaseInsensitive_Matching_Works()
    {
        var mapper = Build();
        var src = new SrcNaming { EMails_ID = "abc" };

        var dst = mapper.Map<SrcNaming, DstNaming>(src);

        Assert.Equal("abc", dst.EmailsId);
    }

    [Fact]
    public void UseGeneratedMaps_False_Falls_Back_To_Runtime()
    {
        // Generated map for SrcSimple sets Age via MapRuntime.Convert. The runtime convention path
        // also converts int->long, so the observable result is identical; we assert correctness only.
        var mapper = Build(useGenerated: false);
        var src = new SrcSimple { Name = "x", Age = 7, Id = Guid.Empty };

        var dst = mapper.Map<SrcSimple, DstSimple>(src);

        Assert.Equal("x", dst.Name);
        Assert.Equal(7L, dst.Age);
    }

    [Fact]
    public void Fluent_Customization_Wins_Over_Generated()
    {
        // A generated map exists for SrcSimple -> DstSimple. Register a runtime customization; the
        // fluent resolver must take precedence.
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SrcSimple, DstSimple>()
              .ForMember(d => d.Name, opt => opt.MapFrom(s => "runtime-name"));
        });

        var mapper = config.BuildMapper();
        var src = new SrcSimple { Name = "ignored", Age = 1, Id = Guid.Empty };

        var dst = mapper.Map<SrcSimple, DstSimple>(src);

        Assert.Equal("runtime-name", dst.Name);
    }

    [Fact]
    public void Null_Source_Returns_Default()
    {
        var mapper = Build();
        var dst = mapper.Map<SrcSimple, DstSimple>(null!);
        Assert.Null(dst);
    }

    [Fact]
    public void Object_Typed_Map_Uses_Generated_Wrapper()
    {
        var mapper = Build();
        object src = new SrcSimple { Name = "obj", Age = 3, Id = Guid.Empty };

        var dst = mapper.Map<DstSimple>(src);

        Assert.Equal("obj", dst.Name);
        Assert.Equal(3L, dst.Age);
    }
}