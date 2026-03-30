using System;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Defines a reusable grouping of mapping configuration similar to AutoMapper profiles.
    /// </summary>
    public abstract class Profile
    {
        private MapperConfiguration? _configuration;

        internal void ApplyTo(MapperConfiguration configuration)
        {
            _configuration = configuration;

            try
            {
                Configure();
            }
            finally
            {
                _configuration = null;
            }
        }

        /// <summary>
        /// Configures mappings contained in this profile.
        /// </summary>
        protected abstract void Configure();

        /// <summary>
        /// Creates a map while the profile is being applied to a configuration.
        /// </summary>
        protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("Profiles can only create maps while being applied to a mapper configuration.");
            }

            return _configuration.CreateMapExpression<TSource, TDestination>();
        }
    }
}
