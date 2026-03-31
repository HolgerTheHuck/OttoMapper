using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Stores map definitions and builds mapper instances from them.
    /// </summary>
    public class MapperConfiguration : IMapperConfiguration
    {
        internal readonly List<TypeMap> TypeMaps = new List<TypeMap>();

        /// <summary>
        /// Gets or sets a value indicating whether non-collection maps must be registered explicitly.
        /// </summary>
        public bool RequireExplicitMaps { get; set; }

        /// <summary>
        /// Initializes a new empty mapper configuration.
        /// </summary>
        public MapperConfiguration()
        {
        }

        /// <summary>
        /// Initializes a new mapper configuration and applies the provided configuration callback.
        /// </summary>
        public MapperConfiguration(Action<MapperConfiguration> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            configure(this);
        }

        /// <inheritdoc />
        public IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(Action<IMappingExpression<TSource, TDestination>>? mapping = null)
        {
            var expression = CreateMapExpression<TSource, TDestination>();
            mapping?.Invoke(expression);
            return expression;
        }

        /// <summary>
        /// Adds a profile type to this configuration.
        /// </summary>
        public void AddProfile<TProfile>() where TProfile : Profile, new()
        {
            AddProfile(new TProfile());
        }

        /// <summary>
        /// Adds a profile instance to this configuration.
        /// </summary>
        public void AddProfile(Profile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            profile.ApplyTo(this);
        }

        /// <summary>
        /// Scans the provided assemblies for profile types and applies them.
        /// </summary>
        public void AddMaps(params Assembly[] assemblies)
        {
            if (assemblies == null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            foreach (var assembly in assemblies.Where(a => a != null))
            {
                foreach (var profileType in assembly.GetTypes().Where(t => typeof(Profile).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    var profile = Activator.CreateInstance(profileType) as Profile;
                    if (profile == null)
                    {
                        throw new InvalidOperationException($"Could not create profile instance for '{profileType.FullName}'. Ensure it has a public parameterless constructor.");
                    }

                    AddProfile(profile);
                }
            }
        }

        /// <inheritdoc />
        public IMapper BuildMapper(bool warmUp = true)
        {
            var mapper = new Mapper(this);
            if (warmUp)
            {
                foreach (var tm in TypeMaps.ToArray())
                {
                    mapper.PrepareMap(tm.SourceType, tm.DestinationType);
                }
            }

            return mapper;
        }

        /// <inheritdoc />
        public IMapper CreateMapper()
        {
            return BuildMapper();
        }

        /// <inheritdoc />
        public void AssertConfigurationIsValid()
        {
            var errors = new List<string>();

            foreach (var typeMap in TypeMaps)
            {
                if (typeMap.CustomConverter != null || typeMap.TypedCustomConverter != null)
                {
                    continue;
                }

                foreach (var destinationProperty in typeMap.DestinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!destinationProperty.CanWrite)
                    {
                        continue;
                    }

                    if (typeMap.IgnoredMembers.Contains(destinationProperty.Name))
                    {
                        continue;
                    }

                    if (typeMap.PathMaps.Any(p => p.Path.StartsWith(destinationProperty.Name + ".", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    if (typeMap.MemberResolvers.ContainsKey(destinationProperty.Name) || typeMap.TypedMemberResolvers.ContainsKey(destinationProperty.Name))
                    {
                        continue;
                    }

                    var sourceProperty = typeMap.SourceType.GetProperty(destinationProperty.Name, BindingFlags.Public | BindingFlags.Instance);
                    if (sourceProperty == null || !sourceProperty.CanRead)
                    {
                        errors.Add($"Missing source member for '{typeMap.SourceType.Name}.{destinationProperty.Name}' -> '{typeMap.DestinationType.Name}.{destinationProperty.Name}'.");
                        continue;
                    }

                    if (sourceProperty.PropertyType == destinationProperty.PropertyType)
                    {
                        continue;
                    }

                    if (MappingHelpers.IsEnumerable(sourceProperty.PropertyType) && MappingHelpers.IsEnumerable(destinationProperty.PropertyType))
                    {
                        var sourceElementType = MappingHelpers.GetEnumerableElementType(sourceProperty.PropertyType);
                        var destinationElementType = MappingHelpers.GetEnumerableElementType(destinationProperty.PropertyType);

                        if (sourceElementType == destinationElementType)
                        {
                            continue;
                        }

                        if (sourceElementType == null || destinationElementType == null || GetTypeMap(sourceElementType, destinationElementType) == null)
                        {
                            errors.Add($"Missing collection element map for '{sourceProperty.PropertyType.Name}' -> '{destinationProperty.PropertyType.Name}' on '{typeMap.SourceType.Name}' -> '{typeMap.DestinationType.Name}'.");
                        }

                        continue;
                    }

                    if (MappingHelpers.IsSimpleType(sourceProperty.PropertyType) || MappingHelpers.IsSimpleType(destinationProperty.PropertyType))
                    {
                        errors.Add($"Incompatible member types for '{typeMap.SourceType.Name}.{sourceProperty.Name}' -> '{typeMap.DestinationType.Name}.{destinationProperty.Name}'.");
                        continue;
                    }

                    if (RequireExplicitMaps && GetTypeMap(sourceProperty.PropertyType, destinationProperty.PropertyType) == null)
                    {
                        errors.Add($"Missing explicit map for nested type '{sourceProperty.PropertyType.Name}' -> '{destinationProperty.PropertyType.Name}'.");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }
        }

        internal TypeMap? GetTypeMap(Type source, Type destination)
        {
            return TypeMaps.FirstOrDefault(tm => tm.SourceType == source && tm.DestinationType == destination);
        }

        internal IMappingExpression<TSource, TDestination> CreateMapExpression<TSource, TDestination>()
        {
            var typeMap = GetTypeMap(typeof(TSource), typeof(TDestination));
            if (typeMap == null)
            {
                typeMap = new TypeMap(typeof(TSource), typeof(TDestination));
                TypeMaps.Add(typeMap);
            }

            return new MappingExpression<TSource, TDestination>(this, typeMap);
        }
    }
}
