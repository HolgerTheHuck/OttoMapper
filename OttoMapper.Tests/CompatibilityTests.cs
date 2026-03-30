using Microsoft.Extensions.DependencyInjection;
using OttoMapper.Extensions;
using OttoMapper.Mapping;
using Xunit;

namespace OttoMapper.Tests;

public class CompatibilityTests
{
    [Fact]
    public void ForMember_MapFrom_And_Ignore_Should_Work()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>()
                .ForMember(d => d.Name, opt => opt.MapFrom(s => s.DisplayName))
                .ForMember(d => d.Ignored, opt => opt.Ignore());
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<SourceDto, DestinationDto>(new SourceDto { DisplayName = "otto", Ignored = "source" });

        Assert.Equal("otto", result.Name);
        Assert.Null(result.Ignored);
    }

    [Fact]
    public void Condition_Should_Skip_Assignment_When_False()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>()
                .ForMember(d => d.Name, opt =>
                {
                    opt.Condition(s => !string.IsNullOrWhiteSpace(s.DisplayName));
                    opt.MapFrom(s => s.DisplayName);
                });
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<SourceDto, DestinationDto>(new SourceDto { DisplayName = "" });

        Assert.Null(result.Name);
    }

    [Fact]
    public void NullSubstitute_Should_Apply_When_Value_Is_Null()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>()
                .ForMember(d => d.Name, opt =>
                {
                    opt.MapFrom(s => s.DisplayName);
                    opt.NullSubstitute("fallback");
                });
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<SourceDto, DestinationDto>(new SourceDto { DisplayName = null });

        Assert.Equal("fallback", result.Name);
    }

    [Fact]
    public void Map_Object_To_Generic_Destination_Should_Work()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>();
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<DestinationDto>(new SourceDto { Name = "generic-object" });

        Assert.Equal("generic-object", result.Name);
    }

    [Fact]
    public void Condition_With_Destination_Should_Use_Destination_State()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationWithFlag>()
                .ConstructUsing(s => new DestinationWithFlag { AllowName = false })
                .ForMember(d => d.AllowName, opt => opt.Ignore())
                .ForMember(d => d.Name, opt =>
                {
                    opt.Condition((s, d) => d.AllowName);
                    opt.MapFrom(s => s.DisplayName);
                });
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<SourceDto, DestinationWithFlag>(new SourceDto { DisplayName = "blocked" });

        Assert.Null(result.Name);
    }

    [Fact]
    public void ReverseMap_Should_Create_Reverse_Configuration_For_Symmetric_Members()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<ReverseSource, ReverseDestination>().ReverseMap();
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<ReverseDestination, ReverseSource>(new ReverseDestination { Name = "reverse" });

        Assert.Equal("reverse", result.Name);
    }

    [Fact]
    public void RequireExplicitMaps_Should_Validate_Nested_Mappings()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.RequireExplicitMaps = true;
            cfg.CreateMap<OuterSource, OuterDestination>();
        });

        var ex = Assert.Throws<InvalidOperationException>(() => config.AssertConfigurationIsValid());
        Assert.Contains("Missing explicit map", ex.Message);
    }

    [Fact]
    public void AddOttoMapper_Should_Register_Profile_From_Assembly()
    {
        var services = new ServiceCollection();
        services.AddOttoMapper(typeof(TestProfile).Assembly);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var result = mapper.Map<ProfileSource, ProfileDestination>(new ProfileSource { Name = "profile" });

        Assert.Equal("profile", result.Name);
    }

    [Fact]
    public void Config_Should_Throw_When_Modified_After_BuildMapper()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>();
        });

        config.BuildMapper();

        Assert.True(config.IsFrozen);
        var ex = Assert.Throws<InvalidOperationException>(() => config.CreateMap<ReverseSource, ReverseDestination>());
        Assert.Contains("BuildMapper", ex.Message);
    }

    private sealed class TestProfile : Profile
    {
        protected override void Configure()
        {
            CreateMap<ProfileSource, ProfileDestination>();
        }
    }

    private sealed class SourceDto
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Ignored { get; set; }
    }

    private sealed class DestinationDto
    {
        public string? Name { get; set; }
        public string? Ignored { get; set; }
    }

    private sealed class ReverseSource
    {
        public string? Name { get; set; }
    }

    private sealed class ReverseDestination
    {
        public string? Name { get; set; }
    }

    private sealed class DestinationWithFlag
    {
        public bool AllowName { get; set; }
        public string? Name { get; set; }
    }

    private sealed class OuterSource
    {
        public InnerSource? Inner { get; set; }
    }

    private sealed class OuterDestination
    {
        public InnerDestination? Inner { get; set; }
    }

    private sealed class InnerSource
    {
        public string? Value { get; set; }
    }

    private sealed class InnerDestination
    {
        public string? Value { get; set; }
    }

    private sealed class ProfileSource
    {
        public string? Name { get; set; }
    }

    private sealed class ProfileDestination
    {
        public string? Name { get; set; }
    }
}
