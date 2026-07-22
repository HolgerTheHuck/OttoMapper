using Xunit;
using OttoMapper.Mapping;
using OttoMapper.Mapping.Generated;

namespace OttoMapper.Tests;

// GeneratedMapRegistry is process-global, so these tests must not run in parallel with each other.
[Collection(nameof(GeneratedMapRegistryCollection))]
public class GeneratedMapPrecedenceTests
{
    // Sample types: Source has Name; Dest has Name + Marker.
    // The "generated" delegate sets Marker = "GEN". Runtime convention leaves Marker null.
    // That difference lets us detect which path was taken.

    private sealed class GenSource
    {
        public string? Name { get; set; }
    }

    private sealed class GenDest
    {
        public string? Name { get; set; }
        public string? Marker { get; set; }
    }

    public GeneratedMapPrecedenceTests()
    {
        GeneratedMapRegistry.Clear();
    }

    private static IMapper Build(bool useGenerated = true)
    {
        var config = new MapperConfiguration
        {
            UseGeneratedMaps = useGenerated
        };
        return config.BuildMapper(warmUp: false);
    }

    [Fact]
    public void Generated_Map_Wins_When_No_Runtime_TypeMap_Registered()
    {
        GeneratedMapRegistry.Register<GenSource, GenDest>(s => new GenDest { Name = s.Name, Marker = "GEN" });

        var mapper = Build();
        var result = mapper.Map<GenSource, GenDest>(new GenSource { Name = "otto" });

        Assert.Equal("otto", result.Name);
        Assert.Equal("GEN", result.Marker);
    }

    [Fact]
    public void Runtime_TypeMap_With_Customization_Wins_Over_Generated()
    {
        // Register a generated map that would set Marker = "GEN".
        GeneratedMapRegistry.Register<GenSource, GenDest>(s => new GenDest { Name = "GEN-NAME", Marker = "GEN" });

        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<GenSource, GenDest>()
              .ForMember(d => d.Name, opt => opt.MapFrom(s => "runtime-name"));
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<GenSource, GenDest>(new GenSource { Name = "ignored" });

        // The fluent resolver must win: Name comes from the runtime resolver, Marker is NOT "GEN".
        Assert.Equal("runtime-name", result.Name);
        Assert.NotEqual("GEN", result.Marker);
    }

    [Fact]
    public void Runtime_TypeMap_Without_Customization_Uses_Generated()
    {
        GeneratedMapRegistry.Register<GenSource, GenDest>(s => new GenDest { Name = s.Name, Marker = "GEN" });

        // A bare CreateMap with no customizations is eligible for the generated fast path.
        var config = new MapperConfiguration(cfg => cfg.CreateMap<GenSource, GenDest>());
        var mapper = config.BuildMapper();

        var result = mapper.Map<GenSource, GenDest>(new GenSource { Name = "otto" });

        Assert.Equal("GEN", result.Marker);
    }

    [Fact]
    public void UseGeneratedMaps_False_Disables_Generated_Path()
    {
        GeneratedMapRegistry.Register<GenSource, GenDest>(s => new GenDest { Name = s.Name, Marker = "GEN" });

        var mapper = Build(useGenerated: false);
        var result = mapper.Map<GenSource, GenDest>(new GenSource { Name = "otto" });

        // Falls back to runtime convention: Name copied, Marker left default (null).
        Assert.Equal("otto", result.Name);
        Assert.Null(result.Marker);
    }

    [Fact]
    public void Object_Typed_Map_Also_Uses_Generated_When_Eligible()
    {
        GeneratedMapRegistry.Register<GenSource, GenDest>(s => new GenDest { Name = s.Name, Marker = "GEN" });

        var mapper = Build();
        object source = new GenSource { Name = "otto" };
        var result = mapper.Map<GenDest>(source);

        Assert.Equal("otto", result.Name);
        Assert.Equal("GEN", result.Marker);
    }

    [Fact]
    public void Empty_Registry_Is_NoOp_And_Uses_Runtime_Convention()
    {
        // No registration at all.
        var mapper = Build();
        var result = mapper.Map<GenSource, GenDest>(new GenSource { Name = "otto" });

        Assert.Equal("otto", result.Name);
        Assert.Null(result.Marker);
    }

    [Fact]
    public void NonDefault_CaseSensitivity_Disables_Generated_Path()
    {
        GeneratedMapRegistry.Register<GenSource, GenDest>(s => new GenDest { Name = s.Name, Marker = "GEN" });

        var config = new MapperConfiguration { CaseInsensitiveMapping = false };
        var mapper = config.BuildMapper();
        var result = mapper.Map<GenSource, GenDest>(new GenSource { Name = "otto" });

        // Generated map ineligible because the global flag diverges from the generator's compile-time default.
        Assert.Null(result.Marker);
        Assert.Equal("otto", result.Name);
    }
}

[CollectionDefinition(nameof(GeneratedMapRegistryCollection))]
public class GeneratedMapRegistryCollection { }