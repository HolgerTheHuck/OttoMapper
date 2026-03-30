using OttoMapper.Mapping;
using Xunit;

namespace OttoMapper.Tests;

public class PathAndHooksTests
{
    [Fact]
    public void ForPath_Should_Set_Nested_Destination_Member()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<PathSource, PathDestination>()
                .ForPath(d => d.Inner.Name, opt => opt.MapFrom(s => s.Name));
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<PathSource, PathDestination>(new PathSource { Name = "nested" });

        var inner = Assert.IsType<PathInnerDestination>(result.Inner);
        Assert.Equal("nested", inner.Name);
    }

    [Fact]
    public void BeforeMap_And_AfterMap_Should_Run()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<HookSource, HookDestination>()
                .BeforeMap((s, d) => d.Value = "before")
                .AfterMap((s, d) => d.Value = d.Value + ":after");
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<HookSource, HookDestination>(new HookSource { Value = "source" });

        Assert.Equal("source:after", result.Value);
    }

    private sealed class PathSource
    {
        public string? Name { get; set; }
    }

    private sealed class PathDestination
    {
        public PathInnerDestination? Inner { get; set; }
    }

    private sealed class PathInnerDestination
    {
        public string? Name { get; set; }
    }

    private sealed class HookSource
    {
        public string? Value { get; set; }
    }

    private sealed class HookDestination
    {
        public string? Value { get; set; }
    }
}
