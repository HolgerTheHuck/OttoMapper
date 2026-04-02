# OttoMapper

OttoMapper is a fast object mapping library for .NET with an API that is intentionally close to common AutoMapper usage patterns.

### Why "Otto"?

Otto is a quintessentially German name — and so is the author. OttoMapper is a lightweight alternative to **Auto**Mapper, so think of it as the _Teutonic_ take on object mapping. A pun, basically.

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
- `ConvertUsing(...)`
- `ConstructUsing(...)`
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

OttoMapper is **not** a drop-in replacement for AutoMapper. It targets API compatibility with **AutoMapper 14** for the most common mapping scenarios — `CreateMap`, profiles, `ForMember`/`ForPath`, conditions, reverse maps, hooks, nested and collection mapping — but it deliberately does not aim for full feature parity. If your project uses only the common subset of AutoMapper 14's API, migrating to OttoMapper should require minimal effort.

## License

OttoMapper is licensed under the MIT License. See the `LICENSE` file in the repository root for the full license text.

## Repository

<https://github.com/HolgerTheHuck/OttoMapper>
