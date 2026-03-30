using System;
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Defines runtime mapping operations between source and destination types.
    /// </summary>
    public interface IMapper
    {
        /// <summary>
        /// Maps a source instance to a destination instance.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDestination">The destination type.</typeparam>
        /// <param name="source">The source instance.</param>
        /// <returns>The mapped destination instance.</returns>
        [return: MaybeNull]
        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>
        /// Maps an object instance to the specified destination type.
        /// </summary>
        /// <typeparam name="TDestination">The destination type.</typeparam>
        /// <param name="source">The source instance.</param>
        /// <returns>The mapped destination instance.</returns>
        [return: MaybeNull]
        TDestination Map<TDestination>(object source);

        /// <summary>
        /// Maps an object instance using explicit source and destination types.
        /// </summary>
        /// <param name="source">The source instance.</param>
        /// <param name="sourceType">The runtime source type.</param>
        /// <param name="destinationType">The runtime destination type.</param>
        /// <returns>The mapped destination instance.</returns>
        [return: MaybeNull]
        object Map(object source, Type sourceType, Type destinationType);
    }

    /// <summary>
    /// Defines configuration operations for mapper creation and validation.
    /// </summary>
    public interface IMapperConfiguration
    {
        /// <summary>
        /// Creates or retrieves a map definition between the specified source and destination types.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDestination">The destination type.</typeparam>
        /// <param name="mapping">Optional map customization callback.</param>
        /// <returns>The created mapping expression.</returns>
        IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>(Action<IMappingExpression<TSource, TDestination>>? mapping = null);

        /// <summary>
        /// Validates the registered mapping configuration and throws if invalid.
        /// </summary>
        void AssertConfigurationIsValid();

        /// <summary>
        /// Builds a mapper instance from the current configuration.
        /// </summary>
        /// <param name="warmUp">When set to <c>true</c>, precompiles known maps during construction.</param>
        /// <returns>A configured mapper instance.</returns>
        IMapper BuildMapper(bool warmUp = true);
    }

    /// <summary>
    /// Defines fluent configuration for a single source-to-destination mapping.
    /// </summary>
    public interface IMappingExpression<TSource, TDestination>
    {
        /// <summary>
        /// Configures a destination member using a direct resolver function.
        /// </summary>
        IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Func<TSource, TMember> resolver);

        /// <summary>
        /// Configures a destination member using AutoMapper-style member options.
        /// </summary>
        IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IMemberOptions<TSource, TDestination, TMember>> options);

        /// <summary>
        /// Configures a nested destination path using AutoMapper-style member options.
        /// </summary>
        IMappingExpression<TSource, TDestination> ForPath<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IMemberOptions<TSource, TDestination, TMember>> options);

        /// <summary>
        /// Registers an action that runs before property assignment starts.
        /// </summary>
        IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action);

        /// <summary>
        /// Registers an action that runs after property assignment has completed.
        /// </summary>
        IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action);

        /// <summary>
        /// Replaces the entire mapping operation with a custom converter.
        /// </summary>
        IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter);

        /// <summary>
        /// Uses a custom construction function to create destination instances.
        /// </summary>
        IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> constructor);

        /// <summary>
        /// Creates the reverse mapping definition.
        /// </summary>
        IMappingExpression<TDestination, TSource> ReverseMap();
    }

    /// <summary>
    /// Defines AutoMapper-like member-level mapping options.
    /// </summary>
    public interface IMemberOptions<TSource, TDestination, TMember>
    {
        /// <summary>
        /// Maps the destination member from a custom resolver.
        /// </summary>
        void MapFrom(Func<TSource, TMember> resolver);

        /// <summary>
        /// Applies the member assignment only when the source condition is satisfied.
        /// </summary>
        void Condition(Func<TSource, bool> condition);

        /// <summary>
        /// Applies the member assignment only when the source and destination condition is satisfied.
        /// </summary>
        void Condition(Func<TSource, TDestination, bool> condition);

        /// <summary>
        /// Ignores the destination member during mapping.
        /// </summary>
        void Ignore();

        /// <summary>
        /// Uses a substitute value when the resolved value is <c>null</c>.
        /// </summary>
        void NullSubstitute(TMember value);
    }
}
