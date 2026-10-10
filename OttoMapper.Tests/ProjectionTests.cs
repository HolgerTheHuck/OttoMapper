using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OttoMapper.Mapping;
using Xunit;

namespace OttoMapper.Tests
{
    public class ProjectionTests
    {
        // ---------------- Parity: compiled projection == mapper.Map ----------------

        [Fact]
        public void Projection_Parity_SimpleSameType()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Widget, WidgetDto>());
            var mapper = config.BuildMapper();

            var src = new Widget { Id = 7, Name = "n", Weight = 3, InternalCode = "x" };
            var compiled = mapper.BuildProjection<Widget, WidgetDto>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<Widget, WidgetDto>(src);

            Assert.Equal(mapped.Id, projected.Id);
            Assert.Equal(mapped.Name, projected.Name);
            Assert.Equal((long)mapped.Weight, projected.Weight);
        }

        [Fact]
        public void Projection_Parity_NumericConvert()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<NumericSrc, NumericDst>());
            var mapper = config.BuildMapper();

            var src = new NumericSrc { Count = 5, Price = 12.5m };
            var compiled = mapper.BuildProjection<NumericSrc, NumericDst>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<NumericSrc, NumericDst>(src);

            Assert.Equal(mapped.CountLong, projected.CountLong);
            Assert.Equal(mapped.PriceDouble, projected.PriceDouble);
        }

        [Fact]
        public void Projection_Parity_NestedObject()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Address, AddressDto>();
                cfg.CreateMap<Person, PersonDto>();
            });
            var mapper = config.BuildMapper();

            var src = new Person { Id = 1, Name = "a", Home = new Address { City = "Berlin", Zip = "10115" } };
            var compiled = mapper.BuildProjection<Person, PersonDto>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<Person, PersonDto>(src);

            Assert.Equal(mapped.Home?.City, projected.Home?.City);
            Assert.Equal(mapped.Home?.Zip, projected.Home?.Zip);
        }

        [Fact]
        public void Projection_Parity_NestedObject_Null()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Address, AddressDto>();
                cfg.CreateMap<Person, PersonDto>();
            });
            var mapper = config.BuildMapper();

            var src = new Person { Id = 1, Name = "a", Home = null };
            var compiled = mapper.BuildProjection<Person, PersonDto>().Compile();
            var projected = compiled(src);

            Assert.Null(projected.Home);
        }

        [Fact]
        public void Projection_Parity_CollectionOfNested()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Item, ItemDto>();
                cfg.CreateMap<Order, OrderDto>();
            });
            var mapper = config.BuildMapper();

            var src = new Order
            {
                Id = 9,
                Items = new List<Item>
                {
                    new() { Id = 1, Name = "i1" },
                    new() { Id = 2, Name = "i2" }
                }
            };
            var compiled = mapper.BuildProjection<Order, OrderDto>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<Order, OrderDto>(src);

            Assert.Equal(mapped.Items!.Count, projected.Items!.Count);
            for (int i = 0; i < mapped.Items!.Count; i++)
            {
                Assert.Equal(mapped.Items[i]?.Id, projected.Items[i]?.Id);
                Assert.Equal(mapped.Items[i]?.Name, projected.Items[i]?.Name);
            }
        }

        [Fact]
        public void Projection_Parity_Collection_ArrayTarget()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Item, ItemDto>();
                cfg.CreateMap<OrderArr, OrderArrDto>();
            });
            var mapper = config.BuildMapper();

            var src = new OrderArr { Id = 1, Items = new[] { new Item { Id = 1, Name = "a" }, new Item { Id = 2, Name = "b" } } };
            var compiled = mapper.BuildProjection<OrderArr, OrderArrDto>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<OrderArr, OrderArrDto>(src);

            Assert.Equal(mapped.Items!.Length, projected.Items!.Length);
            Assert.Equal(mapped.Items!.Select(i => i!.Id), projected.Items!.Select(i => i!.Id));
        }

        [Fact]
        public void Projection_Parity_MapFromExpression()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonNameDto>()
                   .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.Name + "!"));
            });
            var mapper = config.BuildMapper();

            var src = new Person { Id = 1, Name = "Otto", Home = null };
            var compiled = mapper.BuildProjection<Person, PersonNameDto>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<Person, PersonNameDto>(src);

            Assert.Equal(mapped.FullName, projected.FullName);
            Assert.Equal("Otto!", projected.FullName);
        }

        [Fact]
        public void Projection_Parity_IgnoreMember()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonDto>()
                   .ForMember(d => d.Name, opt => opt.Ignore());
            });
            var mapper = config.BuildMapper();

            var src = new Person { Id = 1, Name = "x", Home = null };
            var compiled = mapper.BuildProjection<Person, PersonDto>().Compile();
            var projected = compiled(src);
            var mapped = mapper.Map<Person, PersonDto>(src);

            Assert.Null(projected.Name);
            Assert.Equal(mapped.Name, projected.Name);
        }

        [Fact]
        public void Projection_Parity_LinqToObjects_Select()
        {
            // Run the projection through LINQ-to-Objects (not just compiled) to exercise the Select path.
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Item, ItemDto>();
                cfg.CreateMap<Order, OrderDto>();
            });
            var mapper = config.BuildMapper();

            var source = new List<Order>
            {
                new() { Id = 1, Items = new List<Item> { new() { Id = 10, Name = "a" } } },
                new() { Id = 2, Items = new List<Item> { new() { Id = 20, Name = "b" } } },
            }.AsQueryable();

            var dtos = source.ProjectTo<OrderDto>(mapper).OrderBy(d => d.Id).ToList();
            Assert.Equal(2, dtos.Count);
            Assert.Equal(10, dtos[0].Items![0]!.Id);
            Assert.Equal("b", dtos[1].Items![0]!.Name);
        }

        // ---------------- ProjectionNullGuards switch (in-memory path) ----------------

        [Fact]
        public void Projection_GuardsEnabled_NullCollectionMapsToNull()
        {
            // Opt-in for LINQ-to-Objects projections over sources that carry null collections: with
            // ProjectionNullGuards = true the guards return earlier and map to null instead of throwing.
            var config = new MapperConfiguration(cfg =>
            {
                cfg.ProjectionNullGuards = true;
                cfg.CreateMap<Item, ItemDto>();
                cfg.CreateMap<Order, OrderDto>();
            });
            var mapper = config.BuildMapper();

            var compiled = mapper.BuildProjection<Order, OrderDto>().Compile();
            var projected = compiled(new Order { Id = 3, Items = null });

            Assert.Null(projected.Items);
        }

        [Fact]
        public void Projection_GuardsEnabled_NullCollectionElementMapsToNull()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.ProjectionNullGuards = true;
                cfg.CreateMap<Item, ItemDto>();
                cfg.CreateMap<Order, OrderDto>();
            });
            var mapper = config.BuildMapper();

            var source = new List<Order>
            {
                new() { Id = 1, Items = new List<Item> { new() { Id = 10, Name = "a" }, null! } }
            }.AsQueryable();

            var projected = source.ProjectTo<OrderDto>(mapper).Single();
            Assert.Equal(10, projected.Items![0].Id);
            Assert.Null(projected.Items![1]);
        }

        [Fact]
        public void Projection_GuardsDisabled_NoConditionsInExpression()
        {
            // Default: the collection projection expression contains no collection/element null guards (the
            // ones that make EF Core materialize whole entities alongside the projection).
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Item, ItemDto>();
                cfg.CreateMap<Order, OrderDto>();
            });
            var mapper = config.BuildMapper();

            var source = new Order { Id = 1, Items = new List<Item> { new() { Id = 10, Name = "a" } } };
            var compiled = mapper.BuildProjection<Order, OrderDto>().Compile();
            Assert.Equal(10, compiled(source).Items![0].Id);
            Assert.Equal(0, CountConditional(mapper.BuildProjection<Order, OrderDto>().Body));
        }

        private sealed class ConditionalCounter : ExpressionVisitor
        {
            public int Count;
            public override Expression Visit(Expression? node)
            {
                if (node?.NodeType == ExpressionType.Conditional) Count++;
                return base.Visit(node);
            }
        }

        private static int CountConditional(Expression expression)
        {
            var counter = new ConditionalCounter();
            counter.Visit(expression);
            return counter.Count;
        }

        // ---------------- Unprojectable -> ProjectionException ----------------

        [Fact]
        public void Projection_Unprojectable_ConvertUsing_Throws()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonDto>()
                   .ConvertUsing(p => new PersonDto());
            });
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, PersonDto>());
        }

        [Fact]
        public void Projection_Unprojectable_FuncBasedForMember_Throws()
        {
            // The Func-based ForMember overload has no translatable expression body.
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonNameDto>()
                   .ForMember(d => d.FullName, p => "fixed");
            });
            var mapper = config.BuildMapper();

            var ex = Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, PersonNameDto>());
            Assert.Contains("FullName", ex.Message);
        }

        [Fact]
        public void Projection_Unprojectable_Condition_Throws()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonNameDto>()
                   .ForMember(d => d.FullName, opt =>
                   {
                       opt.Condition(s => s.Id > 0);
                       opt.MapFrom(s => s.Name);
                   });
            });
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, PersonNameDto>());
        }

        [Fact]
        public void Projection_Unprojectable_NullSubstitute_Throws()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonNameDto>()
                   .ForMember(d => d.FullName, opt =>
                   {
                       opt.NullSubstitute("fallback");
                       opt.MapFrom(s => s.Name);
                   });
            });
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, PersonNameDto>());
        }

        [Fact]
        public void Projection_Unprojectable_BeforeMap_Throws()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Person, PersonDto>()
                   .BeforeMap((s, d) => { });
            });
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, PersonDto>());
        }

        [Fact]
        public void Projection_Unprojectable_CtorOnlyDestination_Throws()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<Person, CtorOnly>());
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, CtorOnly>());
        }

        [Fact]
        public void Projection_Unprojectable_EnumToString_Throws()
        {
            var config = new MapperConfiguration(cfg => cfg.CreateMap<EnumSrc, EnumDst>());
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<EnumSrc, EnumDst>());
        }

        [Fact]
        public void Projection_Unprojectable_RequireExplicitMissingMap_Throws()
        {
            var config = new MapperConfiguration { RequireExplicitMaps = true };
            var mapper = config.BuildMapper();

            Assert.Throws<ProjectionException>(() => mapper.BuildProjection<Person, PersonDto>());
        }

        // ---------------- Structure: EF-translatable shape ----------------

        [Fact]
        public void Projection_Structure_NoInvokeOrRuntimeHelpers()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Address, AddressDto>();
                cfg.CreateMap<Person, PersonDto>();
                cfg.CreateMap<Person, PersonNameDto>()
                   .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.Name + "!"));
            });
            var mapper = config.BuildMapper();
            var expr = mapper.BuildProjection<Person, PersonDto>();

            var inspector = new TranslationInspector();
            inspector.Visit(expr);

            Assert.True(inspector.HasMemberInit, "projection should use MemberInit");
            Assert.False(inspector.HasInvoke, "projection must not use Expression.Invoke (opaque to EF)");
            Assert.False(inspector.HasMappingHelpersCall, "projection must not call MappingHelpers (opaque to EF)");
        }

        // ---------------- EF Core SQLite: real SQL translation ----------------

        [Fact]
        public void Projection_EfCore_TranslatesAndSelectsOnlyNeededColumns()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<EfDb>()
                .UseSqlite(connection)
                .LogTo(msg => _sqlLog.Add(msg), LogLevel.Information)
                .Options;

            using var db = new EfDb(options);
            db.Database.EnsureCreated();
            db.Widgets.AddRange(
                new WidgetEntity { Id = 1, Name = "A", Weight = 10, InternalCode = "SECRET-1" },
                new WidgetEntity { Id = 2, Name = "B", Weight = 20, InternalCode = "SECRET-2" });
            db.SaveChanges();

            var config = new MapperConfiguration(cfg => cfg.CreateMap<WidgetEntity, WidgetDto>());
            var mapper = config.BuildMapper();

            var dtos = db.Widgets.ProjectTo<WidgetDto>(mapper).OrderBy(d => d.Id).ToList();

            Assert.Equal(2, dtos.Count);
            Assert.Equal("A", dtos[0].Name);
            Assert.Equal(10L, dtos[0].Weight);
            Assert.Equal("B", dtos[1].Name);
            Assert.Equal(20L, dtos[1].Weight);

            var selectSql = string.Join(Environment.NewLine, _sqlLog.Where(m => m.Contains("SELECT", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("Name", selectSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InternalCode", selectSql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Projection_EfCore_NestedNavigationAndMapFrom()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<EfDb>()
                .UseSqlite(connection)
                .LogTo(msg => _sqlLog.Add(msg), LogLevel.Information)
                .Options;

            using var db = new EfDb(options);
            db.Database.EnsureCreated();
            var cat = new CategoryEntity { Id = 1, Label = "Tools" };
            db.Categories.Add(cat);
            db.Widgets.Add(new WidgetEntity { Id = 10, Name = "Hammer", Weight = 5, InternalCode = "X", Category = cat });
            db.SaveChanges();

            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<CategoryEntity, CategoryDto>();
                cfg.CreateMap<WidgetEntity, WidgetFullDto>()
                   .ForMember(d => d.CategoryLabel, opt => opt.MapFrom(s => s.Category != null ? s.Category.Label : null));
            });
            var mapper = config.BuildMapper();

            var dto = db.Widgets.ProjectTo<WidgetFullDto>(mapper).Single();

            Assert.Equal("Hammer", dto.Name);
            Assert.Equal(5L, dto.Weight);
            Assert.Equal("Tools", dto.CategoryLabel);
            Assert.Equal("Tools", dto.Category!.Label); // nested convention projection

            var selectSql = string.Join(Environment.NewLine, _sqlLog.Where(m => m.Contains("SELECT", StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain("InternalCode", selectSql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Projection_EfCore_CollectionProjection_ParityWithHandwrittenSql()
        {
            // Repro of the O1 overfetching report: nested collections (Story -> Nodes -> Options) must not
            // emit null guards that make EF load the navigations a second time as whole entities.
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<StoryDb>()
                .UseSqlite(connection)
                .LogTo(msg => _sqlLog.Add(msg), LogLevel.Information)
                .Options;

            using var db = new StoryDb(options);
            db.Database.EnsureCreated();
            db.Stories.Add(new StoryEntity
            {
                Id = 1,
                Name = "S1",
                Nodes = new List<StoryNodeEntity>
                {
                    new()
                    {
                        Id = 10, Title = "N1",
                        Options = new List<QuizOptionEntity> { new() { Id = 100, Label = "A", IsCorrect = true } }
                    },
                    new()
                    {
                        Id = 20, Title = "N2",
                        Options = new List<QuizOptionEntity>
                        {
                            new() { Id = 200, Label = "B", IsCorrect = false },
                            new() { Id = 300, Label = "C", IsCorrect = false }
                        }
                    },
                }
            });
            db.SaveChanges();

            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<StoryEntity, StoryDetailDto>();
                cfg.CreateMap<StoryNodeEntity, StoryNodeDto>();
                cfg.CreateMap<QuizOptionEntity, QuizOptionDto>();
            });
            var mapper = config.BuildMapper();

            var dto = db.Stories.ProjectTo<StoryDetailDto>(mapper).Single();
            Assert.Equal("S1", dto.Name);
            Assert.Equal(2, dto.Nodes!.Count);
            Assert.Equal("N1", dto.Nodes[0].Title);
            Assert.Single(dto.Nodes[0].Options!);
            Assert.Equal("A", dto.Nodes[0].Options[0].Label);
            Assert.Equal(2, dto.Nodes[1].Options!.Count);

            // SQL parity with a handwritten projection: run the handwritten query first, capture its SQL,
            // then ProjectTo's SQL — both must select the same shape (2 joins, no IsCorrect column).
            _sqlLog.Clear();
            db.Stories.Select(s => new StoryDetailDto
            {
                Id = s.Id,
                Name = s.Name,
                Nodes = s.Nodes.Select(x => new StoryNodeDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Options = x.Options.Select(o => new QuizOptionDto { Id = o.Id, Label = o.Label }).ToList()
                }).ToList()
            }).Single();
            var handwrittenSql = SelectsFromLog();

            _sqlLog.Clear();
            db.Stories.ProjectTo<StoryDetailDto>(mapper).Single();
            var ottoSql = SelectsFromLog();

            Assert.DoesNotContain("IsCorrect", ottoSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InternalText", ottoSql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(handwrittenSql, ottoSql, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true, ignoreAllWhiteSpace: false);
        }

        private string SelectsFromLog() =>
            string.Join(Environment.NewLine, _sqlLog
                .Where(m => m.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                .Select(msg => msg[(msg.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase))..]));

        private readonly List<string> _sqlLog = new();

        // Fixture: nested collections (collection of collections), mirroring the O1 report scenario.

        private sealed class StoryDetailDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public List<StoryNodeDto>? Nodes { get; set; }
        }

        private sealed class StoryNodeDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public List<QuizOptionDto>? Options { get; set; }
        }

        private sealed class QuizOptionDto
        {
            public int Id { get; set; }
            public string? Label { get; set; }
        }

        private sealed class StoryEntity
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public List<StoryNodeEntity> Nodes { get; set; } = new();
        }

        private sealed class StoryNodeEntity
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public string? InternalText { get; set; }
            public List<QuizOptionEntity> Options { get; set; } = new();
        }

        private sealed class QuizOptionEntity
        {
            public int Id { get; set; }
            public string? Label { get; set; }
            public bool IsCorrect { get; set; }
        }

        private sealed class StoryDb : DbContext
        {
            private readonly DbContextOptions<StoryDb> _options;
            public StoryDb(DbContextOptions<StoryDb> options) : base(options) => _options = options;
            public DbSet<StoryEntity> Stories => Set<StoryEntity>();
            public DbSet<StoryNodeEntity> StoryNodes => Set<StoryNodeEntity>();
            public DbSet<QuizOptionEntity> QuizOptions => Set<QuizOptionEntity>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<StoryEntity>(e =>
                {
                    e.HasKey(s => s.Id);
                    e.HasMany(s => s.Nodes).WithOne().HasForeignKey("StoryId");
                });
                modelBuilder.Entity<StoryNodeEntity>(e =>
                {
                    e.HasKey(n => n.Id);
                    e.HasMany(n => n.Options).WithOne().HasForeignKey("NodeId");
                });
                modelBuilder.Entity<QuizOptionEntity>().HasKey(o => o.Id);
            }
        }

        // ---------------- Expression inspector ----------------

        private sealed class TranslationInspector : ExpressionVisitor
        {
            public bool HasMemberInit { get; private set; }
            public bool HasInvoke { get; private set; }
            public bool HasMappingHelpersCall { get; private set; }

            protected override Expression VisitMemberInit(MemberInitExpression node)
            {
                HasMemberInit = true;
                return base.VisitMemberInit(node);
            }

            protected override Expression VisitInvocation(InvocationExpression node)
            {
                HasInvoke = true;
                return base.VisitInvocation(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method?.DeclaringType?.FullName == "OttoMapper.Mapping.MappingHelpers")
                {
                    HasMappingHelpersCall = true;
                }

                return base.VisitMethodCall(node);
            }
        }

        // ---------------- Fixture types ----------------

        private sealed class Widget
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public int Weight { get; set; }
            public string? InternalCode { get; set; }
        }

        private sealed class WidgetDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public long Weight { get; set; }      // int -> long numeric convert
            // Note: Widget.InternalCode is intentionally NOT part of the DTO so projection must not select it.
        }

        private sealed class NumericSrc { public int Count { get; set; } public decimal Price { get; set; } }
        private sealed class NumericDst { public long CountLong { get; set; } public double PriceDouble { get; set; } }

        private sealed class Address { public string? City { get; set; } public string? Zip { get; set; } }
        private sealed class AddressDto { public string? City { get; set; } public string? Zip { get; set; } }

        private sealed class Person { public int Id { get; set; } public string? Name { get; set; } public Address? Home { get; set; } }
        private sealed class PersonDto { public int Id { get; set; } public string? Name { get; set; } public AddressDto? Home { get; set; } }
        private sealed class PersonNameDto { public int Id { get; set; } public string? FullName { get; set; } }

        private sealed class Item { public int Id { get; set; } public string? Name { get; set; } }
        private sealed class ItemDto { public int Id { get; set; } public string? Name { get; set; } }
        private sealed class Order { public int Id { get; set; } public List<Item>? Items { get; set; } }
        private sealed class OrderDto { public int Id { get; set; } public List<ItemDto>? Items { get; set; } }
        private sealed class OrderArr { public int Id { get; set; } public Item[]? Items { get; set; } }
        private sealed class OrderArrDto { public int Id { get; set; } public ItemDto[]? Items { get; set; } }

        private sealed class CtorOnly
        {
            public int Id { get; }
            public CtorOnly(int id) { Id = id; }
        }

        private enum Color { Red, Green, Blue }
        private sealed class EnumSrc { public Color Color { get; set; } }
        private sealed class EnumDst { public string? Color { get; set; } }

        // EF entities
        private sealed class WidgetEntity
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public int Weight { get; set; }
            public string? InternalCode { get; set; }
            public int? CategoryId { get; set; }
            public CategoryEntity? Category { get; set; }
        }

        private sealed class CategoryEntity { public int Id { get; set; } public string? Label { get; set; } }
        private sealed class CategoryDto { public int Id { get; set; } public string? Label { get; set; } }
        private sealed class WidgetFullDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public long Weight { get; set; }
            public string? CategoryLabel { get; set; }
            public CategoryDto? Category { get; set; }
        }

        private sealed class EfDb : DbContext
        {
            private readonly DbContextOptions<EfDb> _options;
            public EfDb(DbContextOptions<EfDb> options) : base(options) => _options = options;
            public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();
            public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<WidgetEntity>().HasKey(w => w.Id);
                modelBuilder.Entity<WidgetEntity>().Property(w => w.InternalCode);
                modelBuilder.Entity<WidgetEntity>().HasOne(w => w.Category)
                    .WithMany().HasForeignKey(w => w.CategoryId);
                modelBuilder.Entity<CategoryEntity>().HasKey(c => c.Id);
            }
        }
    }
}