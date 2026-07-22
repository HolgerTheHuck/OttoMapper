# OttoMapper

OttoMapper is a fast object mapping library for .NET with an API that is intentionally close to common AutoMapper usage patterns.

### Why "Otto"?

Otto is a quintessentially German name — and so is the author. OttoMapper is a lightweight alternative to **Auto**Mapper, so think of it as the _Teutonic_ take on object mapping. A pun, basically.

## Packages

- `OttoMapper.Mapping` - core mapping engine
- `OttoMapper.Extensions` - dependency injection integration for ASP.NET Core
- `OttoMapper.Generator` - **optional** compile-time source generator for convention-based maps

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
- **Compile-time source generator** (optional) for AOT-friendly convention maps
- **Query projection** (`ProjectTo`) for server-side EF Core / IQueryable projection

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

## Compile-time source generator (optional, AOT-friendly)

OttoMapper ships an **optional** Roslyn source generator that compiles convention-based maps at
compile time, avoiding the runtime `Expression.Compile()` cost and enabling AOT/trimming for
static maps. It is fully additive: if you do not reference the generator package, OttoMapper uses
its runtime expression-tree path unchanged (byte-identical behavior).

### Opting in

Reference the generator as an **analyzer** (note `OutputItemType="Analyzer"` and `PrivateAssets="all"`):

```xml
<ItemGroup>
  <ProjectReference Include="..\OttoMapper.Generator\OttoMapper.Generator.csproj"
                    OutputItemType="Analyzer" PrivateAssets="all" />
  <!-- or, from NuGet: -->
  <!-- <PackageReference Include="OttoMapper.Generator" Version="1.2.0" PrivateAssets="all" OutputItemType="Analyzer" /> -->
</ItemGroup>
<ItemGroup>
  <PackageReference Include="OttoMapper.Mapping" Version="1.2.0" />
</ItemGroup>
```

### Declaring a generated map

Decorate the **destination** type with `[AutoMap(typeof(TSource))]`:

```csharp
using OttoMapper.Mapping.Generated;

public class OrderSource { public string? Name { get; set; } public int Quantity { get; set; } }

[AutoMap(typeof(OrderSource))]
public class OrderDestination
{
    public string? Name { get; set; }
    public long Quantity { get; set; }   // int -> long is converted at compile time
    [IgnoreMap] public string? Skip { get; set; }
    [MapSource("Name")] public string? Label { get; set; }
}
```

Then map as usual — the generated delegate is preferred automatically:

```csharp
var mapper = new MapperConfiguration().BuildMapper();
var dto = mapper.Map<OrderSource, OrderDestination>(source);
```

### What the generator handles vs. falls back to runtime

The generator emits a static map only when **every** writable destination property is statically
resolvable: same-type assignment, simple conversions (numeric/enum), collections (same or
convertible elements, or elements mapped by another `[AutoMap]` pair), and nested objects whose
pair is also declared with `[AutoMap]`. Destination types need a public parameterless constructor.

Anything it cannot generate statically — custom resolvers, conditions, `ConvertUsing`,
`ConstructUsing`, hooks, `ForPath`, init-only properties, records without a parameterless
constructor, nested pairs not declared with `[AutoMap]` — produces an `OTTOMAP001` warning and
falls back to the runtime expression-tree path. **Runtime customizations always take precedence**
over a generated map: `CreateMap<S,D>().ForMember(...)` is honored, never shadowed.

### Kill switch

Disable generated maps at runtime even when the generator is referenced:

```csharp
var config = new MapperConfiguration { UseGeneratedMaps = false };
```

Generated maps use the default name-matching rules (case-insensitive, underscore-ignoring). If you
change `MapperConfiguration.CaseInsensitiveMapping` or `IgnoreUnderscoresInPropertyNames` away from
the defaults, generated maps are disabled for that configuration (to avoid divergence).

## Query projection (`ProjectTo`, EF Core / IQueryable)

OttoMapper can translate a configured map into an `Expression<Func<TSource, TDestination>>` that LINQ
providers (e.g. EF Core) translate **server-side** to SQL — so the database does the projection and only
the columns the destination type needs are selected. No EF dependency is added to OttoMapper.Mapping; the
expression is produced and handed to your query provider.

```csharp
using OttoMapper.Mapping;

var config = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>());
var mapper = config.BuildMapper();

// Server-side: EF Core translates this to SELECT [o].[Id], [o].[Name], ... (only the DTO columns)
var dtos = db.Orders.ProjectTo<OrderDto>(mapper).OrderBy(d => d.Name).ToList();
```

Two overloads are available:

- `query.ProjectTo<TDestination>(mapper)` — infers the source element type at runtime (AutoMapper-style).
- `query.ProjectTo<TSource, TDestination>(mapper)` — compile-time type-safe variant.

You can also obtain the expression directly via `mapper.BuildProjection<TSource, TDestination>()` (or the
non-generic `BuildProjection(Type, Type)`) and pass it to `Queryable.Select` yourself.

### What is projectable

- Convention members of the same type (direct assignment).
- Numeric → numeric conversions (`int` → `long`, `decimal` → `double`, incl. nullable wrapping) emitted as
  explicit casts, which EF Core translates.
- Nested objects (recursively projected and inlined, with a null guard).
- Collections via inline `Enumerable.Select(...).ToList()` / `.ToArray()`, with a null guard.
- `opt.MapFrom(s => ...)` **expression** resolvers — the resolver body is inlined into the projection so
  custom SQL-side projections (e.g. `s => s.FirstName + " " + s.LastName`) translate.
- `Ignore`d destination members are skipped.

### What is not projectable (throws `ProjectionException`)

- The `Func`-based `ForMember(d, s => ...)` overload — use `opt.MapFrom(s => ...)` with an expression instead.
- `ConvertUsing`, `ConstructUsing`, `Condition`, `NullSubstitute`, `BeforeMap`, `AfterMap`, `ForPath`/`PathMaps`.
- Enum ↔ string/numeric and string ↔ numeric conversions — provide an explicit `MapFrom(s => ...)` expression.
- Destination types without a public parameterless constructor (records / constructor-only types) —
  materialize the query first and use `Map`.

When a map cannot be projected, `ProjectTo`/`BuildProjection` throws a `ProjectionException` naming the
member or map and a recommended action. For non-projectable maps, materialize the query (`ToList()`) and
map in memory with `mapper.Map<...>` instead.

Projection is expression-based and therefore AOT/trimming-friendly. It is independent of the source
generator: `ProjectTo` rebuilds the projection from the `TypeMap`/conventions (the generator emits compiled
delegates, not expressions, so it is not used for projection).

## Notes

OttoMapper is **not** a drop-in replacement for AutoMapper. It targets API compatibility with **AutoMapper 14** for the most common mapping scenarios — `CreateMap`, profiles, `ForMember`/`ForPath`, conditions, reverse maps, hooks, nested and collection mapping — but it deliberately does not aim for full feature parity. If your project uses only the common subset of AutoMapper 14's API, migrating to OttoMapper should require minimal effort.

## License

OttoMapper is licensed under the MIT License. See the `LICENSE` file in the repository root for the full license text.

## Repository

<https://github.com/HolgerTheHuck/OttoMapper>
