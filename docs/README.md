# OttoMapper

OttoMapper is a fast object mapping library for .NET with an API that is intentionally close to common AutoMapper usage patterns.

## Packages

- `OttoMapper.Mapping` - core mapping engine
- `OttoMapper.Extensions` - dependency injection integration for ASP.NET Core

## Features

- `CreateMap<TSource, TDestination>()`
- `Profile` support
- `RequireExplicitMaps`
- `AssertConfigurationIsValid()`
- nested mapping
- collection mapping
- `ForMember(..., opt => opt.MapFrom(...))`
- `Ignore()`
- `Condition(...)`
- `NullSubstitute(...)`
- `ReverseMap()`
- `ForPath(...)`
- `BeforeMap(...)` and `AfterMap(...)`
- `Map<TDestination>(object source)`

## Basic usage

```csharp
var config = new MapperConfiguration(cfg =>
{
    cfg.RequireExplicitMaps = true;

    cfg.CreateMap<AddressSource, AddressDestination>();
    cfg.CreateMap<OrderSource, OrderDestination>()
        .ForMember(d => d.Name, opt => opt.MapFrom(s => s.DisplayName))
        .ForMember(d => d.Description, opt =>
        {
            opt.Condition(s => !string.IsNullOrWhiteSpace(s.Description));
            opt.NullSubstitute("n/a");
            opt.MapFrom(s => s.Description);
        })
        .ReverseMap();
});

config.AssertConfigurationIsValid();

var mapper = config.BuildMapper();
var dto = mapper.Map<OrderDestination>(source);
```

## ASP.NET Core

```csharp
builder.Services.AddOttoMapper(typeof(MyProfile).Assembly);
```

or

```csharp
builder.Services.AddOttoMapper(cfg =>
{
    cfg.RequireExplicitMaps = true;
}, typeof(MyProfile).Assembly);
```

## Notes

OttoMapper aims to be close enough to AutoMapper for common Web API and DTO mapping scenarios, while staying lightweight and performance-focused. It does not aim for full feature parity.

## License

OttoMapper is licensed under the MIT License. See the `LICENSE` file in the repository root for the full license text.

## Repository

<https://github.com/HolgerHuckfeldt/OttoMapper>
