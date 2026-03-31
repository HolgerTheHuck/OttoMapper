using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OttoMapper.Mapping;

namespace OttoMapper.Extensions
{
    /// <summary>
    /// Provides dependency injection registration helpers for OttoMapper.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers OttoMapper using an optional configuration callback.
        /// </summary>
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, Action<MapperConfiguration>? configure = null)
        {
            var config = new MapperConfiguration();
            configure?.Invoke(config);
            services.AddSingleton(config);
            services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().BuildMapper());
            return services;
        }

        /// <summary>
        /// Registers OttoMapper and scans the specified assemblies for profiles.
        /// </summary>
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, params Assembly[] assemblies)
        {
            return AddOttoMapper(services, null, assemblies);
        }

        /// <summary>
        /// Registers OttoMapper and scans the assemblies containing the specified marker types for profiles.
        /// </summary>
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, params Type[] markerTypes)
        {
            return AddOttoMapper(services, null, markerTypes);
        }

        /// <summary>
        /// Registers OttoMapper, scans the specified assemblies for profiles, and applies additional configuration.
        /// </summary>
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, Action<MapperConfiguration>? configure, params Assembly[] assemblies)
        {
            var config = new MapperConfiguration();

            if (assemblies != null && assemblies.Length > 0)
            {
                config.AddMaps(assemblies);
            }

            configure?.Invoke(config);

            services.AddSingleton(config);
            services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().BuildMapper());
            return services;
        }

        /// <summary>
        /// Registers OttoMapper, scans the assemblies containing the specified marker types for profiles, and applies additional configuration.
        /// </summary>
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, Action<MapperConfiguration>? configure, params Type[] markerTypes)
        {
            if (markerTypes == null)
            {
                throw new ArgumentNullException(nameof(markerTypes));
            }

            var assemblies = new Assembly[markerTypes.Length];
            for (var i = 0; i < markerTypes.Length; i++)
            {
                var markerType = markerTypes[i] ?? throw new ArgumentException("Marker types must not contain null entries.", nameof(markerTypes));
                assemblies[i] = markerType.Assembly;
            }

            return AddOttoMapper(services, configure, assemblies);
        }

        /// <summary>
        /// Registers OttoMapper using an existing configuration instance.
        /// </summary>
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, MapperConfiguration config)
        {
            services.AddSingleton(config);
            services.AddSingleton<IMapper>(sp => config.BuildMapper());
            return services;
        }
    }
}
