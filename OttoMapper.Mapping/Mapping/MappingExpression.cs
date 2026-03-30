using System;
using System.Linq.Expressions;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Provides fluent configuration for a single source-to-destination mapping definition.
    /// </summary>
    public class MappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
    {
        private readonly MapperConfiguration _configuration;
        private readonly TypeMap _typeMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="MappingExpression{TSource, TDestination}"/> class.
        /// </summary>
        public MappingExpression(MapperConfiguration configuration, TypeMap typeMap)
        {
            _configuration = configuration;
            _typeMap = typeMap;
        }

        /// <summary>
        /// Configures a destination member using a direct resolver function.
        /// </summary>
        public IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Func<TSource, TMember> resolver)
        {
            if (destinationMember.Body is MemberExpression mexp)
            {
                var name = mexp.Member.Name;
                Func<object, object> boxed = src => resolver((TSource)src)!;
                _typeMap.MemberResolvers[name] = boxed;
                _typeMap.TypedMemberResolvers[name] = (typeof(TSource), typeof(TMember), resolver);
            }
            else
            {
                throw new ArgumentException("Destination member must be a property access", nameof(destinationMember));
            }

            return this;
        }

        /// <summary>
        /// Configures a destination member using member options.
        /// </summary>
        public IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IMemberOptions<TSource, TDestination, TMember>> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!(destinationMember.Body is MemberExpression mexp))
            {
                throw new ArgumentException("Destination member must be a property access", nameof(destinationMember));
            }

            var memberOptions = new MemberOptions<TSource, TDestination, TMember>(_typeMap, mexp.Member.Name);
            options(memberOptions);
            return this;
        }

        /// <summary>
        /// Configures a nested destination path using member options.
        /// </summary>
        public IMappingExpression<TSource, TDestination> ForPath<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Action<IMemberOptions<TSource, TDestination, TMember>> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var path = PathMap.GetPath(destinationMember);
            var memberOptions = new PathMemberOptions<TSource, TDestination, TMember>(_typeMap, path);
            options(memberOptions);
            return this;
        }

        /// <summary>
        /// Registers an action to run before member assignments.
        /// </summary>
        public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _typeMap.BeforeMapActions.Add((source, destination) => action((TSource)source, (TDestination)destination));
            return this;
        }

        /// <summary>
        /// Registers an action to run after member assignments.
        /// </summary>
        public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _typeMap.AfterMapActions.Add((source, destination) => action((TSource)source, (TDestination)destination));
            return this;
        }

        /// <summary>
        /// Replaces the mapping with a custom converter.
        /// </summary>
        public IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter)
        {
            Func<object, object> boxed = src => converter((TSource)src)!;
            _typeMap.CustomConverter = boxed;
            _typeMap.TypedCustomConverter = converter;
            return this;
        }

        /// <summary>
        /// Uses a custom destination construction function.
        /// </summary>
        public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> constructor)
        {
            Func<object, object> boxed = src => constructor((TSource)src)!;
            _typeMap.ConstructUsing = boxed;
            _typeMap.TypedConstructUsing = constructor;
            return this;
        }

        /// <summary>
        /// Creates the reverse mapping definition.
        /// </summary>
        public IMappingExpression<TDestination, TSource> ReverseMap()
        {
            return _configuration.CreateMapExpression<TDestination, TSource>();
        }
    }

    internal class MemberOptions<TSource, TDestination, TMember> : IMemberOptions<TSource, TDestination, TMember>
    {
        private readonly TypeMap _typeMap;
        private readonly string _memberName;

        public MemberOptions(TypeMap typeMap, string memberName)
        {
            _typeMap = typeMap;
            _memberName = memberName;
        }

        public void MapFrom(Func<TSource, TMember> resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            Func<object, object> boxed = src => resolver((TSource)src)!;
            _typeMap.MemberResolvers[_memberName] = boxed;
            _typeMap.TypedMemberResolvers[_memberName] = (typeof(TSource), typeof(TMember), resolver);
            _typeMap.IgnoredMembers.Remove(_memberName);
        }

        public void Condition(Func<TSource, bool> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            _typeMap.MemberConditions[_memberName] = src => condition((TSource)src);
        }

        public void Condition(Func<TSource, TDestination, bool> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            _typeMap.MemberConditionsWithDestination[_memberName] = (src, dest) => condition((TSource)src, (TDestination)dest);
        }

        public void Ignore()
        {
            _typeMap.MemberResolvers.Remove(_memberName);
            _typeMap.TypedMemberResolvers.Remove(_memberName);
            _typeMap.IgnoredMembers.Add(_memberName);
        }

        public void NullSubstitute(TMember value)
        {
            _typeMap.NullSubstitutes[_memberName] = value!;
        }
    }

    internal class PathMemberOptions<TSource, TDestination, TMember> : IMemberOptions<TSource, TDestination, TMember>
    {
        private readonly PathMap _pathMap;

        public PathMemberOptions(TypeMap typeMap, string path)
        {
            _pathMap = typeMap.PathMaps.Find(p => p.Path == path) ?? new PathMap(path);
            if (!typeMap.PathMaps.Contains(_pathMap))
            {
                typeMap.PathMaps.Add(_pathMap);
            }
        }

        public void MapFrom(Func<TSource, TMember> resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            _pathMap.Resolver = src => resolver((TSource)src)!;
            _pathMap.Ignore = false;
        }

        public void Condition(Func<TSource, bool> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            _pathMap.Condition = src => condition((TSource)src);
        }

        public void Condition(Func<TSource, TDestination, bool> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            _pathMap.ConditionWithDestination = (src, dest) => condition((TSource)src, (TDestination)dest);
        }

        public void Ignore()
        {
            _pathMap.Ignore = true;
        }

        public void NullSubstitute(TMember value)
        {
            _pathMap.NullSubstitute = value;
        }
    }
}
