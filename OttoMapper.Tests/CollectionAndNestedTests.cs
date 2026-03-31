using OttoMapper.Mapping;
using Xunit;

namespace OttoMapper.Tests;

public class CollectionAndNestedTests
{
    [Fact]
    public void Nested_And_Collection_Mapping_Should_Work_With_Explicit_Maps()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.RequireExplicitMaps = true;
            cfg.CreateMap<AddressSource, AddressDestination>();
            cfg.CreateMap<OrderSource, OrderDestination>();
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<OrderSource, OrderDestination>(new OrderSource
        {
            Address = new AddressSource { City = "Berlin" },
            PreviousAddresses = new List<AddressSource>
            {
                new AddressSource { City = "Munich" },
                new AddressSource { City = "Hamburg" }
            }
        });

        Assert.NotNull(result.Address);
        Assert.Equal("Berlin", result.Address!.City);
        Assert.NotNull(result.PreviousAddresses);
        Assert.Equal(2, result.PreviousAddresses!.Length);
        Assert.Equal("Hamburg", result.PreviousAddresses[1].City);
    }

    [Fact]
    public void Null_Nested_Source_Property_Should_Map_To_Null_Destination()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<AddressSource, AddressDestination>();
            cfg.CreateMap<OrderSource, OrderDestination>();
        });

        var mapper = config.BuildMapper();
        var result = mapper.Map<OrderSource, OrderDestination>(new OrderSource
        {
            Address = null,
            PreviousAddresses = null
        });

        Assert.Null(result.Address);
        // Null collections map to empty collections (consistent with AutoMapper behavior)
        Assert.NotNull(result.PreviousAddresses);
        Assert.Empty(result.PreviousAddresses!);
    }

    [Fact]
    public void Map_Should_Update_Existing_Destination_Collection()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<AddressSource, AddressDestination>();
        });

        var mapper = config.CreateMapper();
        var destination = new List<AddressDestination>
        {
            new AddressDestination { City = "Old" }
        };

        var result = mapper.Map(
            new List<AddressSource>
            {
                new AddressSource { City = "Berlin" },
                new AddressSource { City = "Hamburg" }
            },
            destination);

        Assert.Same(destination, result);
        Assert.Equal(2, destination.Count);
        Assert.Equal("Berlin", destination[0].City);
        Assert.Equal("Hamburg", destination[1].City);
    }

    private sealed class OrderSource
    {
        public AddressSource? Address { get; set; }
        public List<AddressSource>? PreviousAddresses { get; set; }
    }

    private sealed class OrderDestination
    {
        public AddressDestination? Address { get; set; }
        public AddressDestination[]? PreviousAddresses { get; set; }
    }

    private sealed class AddressSource
    {
        public string? City { get; set; }
    }

    private sealed class AddressDestination
    {
        public string? City { get; set; }
    }
}
