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

        var mapper = config.CreateMapper();
        var result = mapper.Map<DestinationDto>(new SourceDto { Name = "generic-object" });

        Assert.Equal("generic-object", result.Name);
    }

    [Fact]
    public void CreateMapper_Should_Be_Available_As_AutoMapper_Like_Alias()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>();
        });

        var mapper = config.CreateMapper();
        var result = mapper.Map<SourceDto, DestinationDto>(new SourceDto { Name = "alias" });

        Assert.Equal("alias", result.Name);
    }

    [Fact]
    public void Map_Should_Support_Existing_Destination_Instance()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>();
        });

        var mapper = config.CreateMapper();
        var destination = new DestinationDto { Name = "old", Ignored = "keep" };
        var result = mapper.Map(new SourceDto { Name = "updated", Ignored = "copied" }, destination);

        Assert.Same(destination, result);
        Assert.Equal("updated", destination.Name);
        Assert.Equal("copied", destination.Ignored);
    }

    [Fact]
    public void Map_Object_Should_Support_Existing_Destination_Instance()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>();
        });

        var mapper = config.CreateMapper();
        object destination = new DestinationDto { Name = "old", Ignored = "keep" };
        var result = mapper.Map(new SourceDto { Name = "runtime", Ignored = "updated" }, destination);

        Assert.Same(destination, result);
        Assert.Equal("runtime", ((DestinationDto)destination).Name);
        Assert.Equal("updated", ((DestinationDto)destination).Ignored);
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
    public void ReverseMap_Should_Copy_Simple_MapFrom_Member()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<ReverseMemberSource, ReverseMemberDestination>()
                .ForMember(d => d.Name, opt => opt.MapFrom(s => s.DisplayName))
                .ReverseMap();
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<ReverseMemberDestination, ReverseMemberSource>(new ReverseMemberDestination { Name = "mapped-back" });

        Assert.Equal("mapped-back", result.DisplayName);
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
    public void Map_Should_Convert_Enum_To_Int_And_Back()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<EnumSource, EnumDestination>();
            cfg.CreateMap<EnumDestination, EnumSource>();
        });

        var mapper = config.CreateMapper();
        var enumToInt = mapper.Map<EnumSource, EnumDestination>(new EnumSource { Status = PreisTyp.Premium });
        var intToEnum = mapper.Map<EnumDestination, EnumSource>(new EnumDestination { Status = 2 });

        Assert.Equal(2, enumToInt.Status);
        Assert.Equal(PreisTyp.Premium, intToEnum.Status);
    }

    [Fact]
    public void Map_Should_Create_Destination_Without_Public_Parameterless_Constructor()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationWithoutPublicCtor>();
        });

        var mapper = config.CreateMapper();
        var result = mapper.Map<SourceDto, DestinationWithoutPublicCtor>(new SourceDto { Name = "constructed" });

        Assert.NotNull(result);
        Assert.Equal("constructed", result.Name);
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
    public void Config_Should_Allow_Modification_After_BuildMapper()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>();
        });

        config.BuildMapper();
        config.CreateMap<ReverseSource, ReverseDestination>();

        var mapper = config.BuildMapper();
        var result = mapper.Map<ReverseSource, ReverseDestination>(new ReverseSource { Name = "later" });

        Assert.Equal("later", result.Name);
    }

    [Fact]
    public void BuildMapper_Should_Not_Force_Validation_For_Unvalidated_Config()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.RequireExplicitMaps = true;
            cfg.CreateMap<OuterSource, OuterDestination>();
        });

        var mapper = config.BuildMapper(warmUp: false);

        Assert.NotNull(mapper);
    }

    [Fact]
    public void AddOttoMapper_Should_Register_Profile_From_Marker_Type()
    {
        var services = new ServiceCollection();
        services.AddOttoMapper(typeof(TestProfile));

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var result = mapper.Map<ProfileSource, ProfileDestination>(new ProfileSource { Name = "marker" });

        Assert.Equal("marker", result.Name);
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

    private sealed class ReverseMemberSource
    {
        public string? DisplayName { get; set; }
    }

    private sealed class ReverseMemberDestination
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

    private sealed class EnumSource
    {
        public PreisTyp Status { get; set; }
    }

    private sealed class EnumDestination
    {
        public int Status { get; set; }
    }

    private enum PreisTyp
    {
        Basic = 1,
        Premium = 2
    }

    private sealed class DestinationWithoutPublicCtor
    {
        private DestinationWithoutPublicCtor()
        {
        }

        public string? Name { get; set; }
    }
}
