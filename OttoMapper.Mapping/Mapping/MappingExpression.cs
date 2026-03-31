using System;
using System.Collections.Generic;
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
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            Expression<Func<TSource, TMember>> resolverExpression = source => resolver(source);
            ApplyMemberMap(destinationMember, resolverExpression);
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
            var reverseExpression = _configuration.CreateMapExpression<TDestination, TSource>();
            if (!(reverseExpression is MappingExpression<TDestination, TSource> reverseConcreteExpression))
            {
                return reverseExpression;
            }

            foreach (var ignoredMember in _typeMap.IgnoredMembers)
            {
                if (_typeMap.SourceType.GetProperty(ignoredMember) != null)
                {
                    reverseExpression.ForMember(BuildPropertyLambda<TSource>(ignoredMember), opt => opt.Ignore());
                }
            }

            foreach (var reversePath in _typeMap.ReverseSourcePaths)
            {
                var sourceMember = _typeMap.SourceType.GetProperty(reversePath.Value);
                var destinationMember = _typeMap.DestinationType.GetProperty(reversePath.Key);
                if (sourceMember == null || destinationMember == null || sourceMember.PropertyType != destinationMember.PropertyType)
                {
                    continue;
                }

                CloneSimpleReverseMember(sourceMember, destinationMember);
            }

            return reverseExpression;
        }

        private void ApplyMemberMap<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Expression<Func<TSource, TMember>> resolver)
        {
            if (!(destinationMember.Body is MemberExpression mexp))
            {
                throw new ArgumentException("Destination member must be a property access", nameof(destinationMember));
            }

            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            var name = mexp.Member.Name;
            var compiledResolver = resolver.Compile();
            Func<object, object> boxed = src => compiledResolver((TSource)src)!;
            _typeMap.MemberResolvers[name] = boxed;
            _typeMap.TypedMemberResolvers[name] = (typeof(TSource), typeof(TMember), compiledResolver);

            var sourcePath = MappingExpressionUtilities.GetSourcePath(resolver);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                _typeMap.ReverseSourcePaths[name] = sourcePath;
            }
        }

        private void CloneSimpleReverseMember(System.Reflection.PropertyInfo sourceMember, System.Reflection.PropertyInfo destinationMember)
        {
            var method = typeof(MappingExpression<TSource, TDestination>).GetMethod(nameof(ApplyReverseMember), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(TDestination), typeof(TSource), sourceMember.PropertyType);
            method.Invoke(null, new object[] { _configuration, sourceMember.Name, destinationMember.Name });
        }

        private static void ApplyReverseMember<TReverseSource, TReverseDestination, TMember>(MapperConfiguration configuration, string reverseDestinationMemberName, string reverseSourceMemberName)
        {
            var reverseExpression = configuration.CreateMapExpression<TReverseSource, TReverseDestination>();
            if (!(reverseExpression is MappingExpression<TReverseSource, TReverseDestination> reverseConcreteExpression))
            {
                return;
            }

            var destinationLambda = BuildPropertyLambda<TReverseDestination, TMember>(reverseDestinationMemberName);
            var sourceLambda = BuildPropertyLambda<TReverseSource, TMember>(reverseSourceMemberName);
            reverseConcreteExpression.ApplyMemberMap(destinationLambda, sourceLambda);
        }

        private static Expression<Func<T, TMember>> BuildPropertyLambda<T, TMember>(string memberName)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var body = Expression.Property(parameter, memberName);
            return Expression.Lambda<Func<T, TMember>>(body, parameter);
        }

        private static Expression<Func<T, object>> BuildPropertyLambda<T>(string memberName)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var body = Expression.Convert(Expression.Property(parameter, memberName), typeof(object));
            return Expression.Lambda<Func<T, object>>(body, parameter);
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

        public void MapFrom(Expression<Func<TSource, TMember>> resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            var compiledResolver = resolver.Compile();
            Func<object, object> boxed = src => compiledResolver((TSource)src)!;
            _typeMap.MemberResolvers[_memberName] = boxed;
            _typeMap.TypedMemberResolvers[_memberName] = (typeof(TSource), typeof(TMember), compiledResolver);
            _typeMap.IgnoredMembers.Remove(_memberName);
            var sourcePath = MappingExpressionUtilities.GetSourcePath(resolver);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                _typeMap.ReverseSourcePaths[_memberName] = sourcePath;
            }
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

        public void MapFrom(Expression<Func<TSource, TMember>> resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            var compiledResolver = resolver.Compile();
            _pathMap.Resolver = src => compiledResolver((TSource)src)!;
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

    internal static class MappingExpressionUtilities
    {
        public static string GetSourcePath<TSource, TMember>(Expression<Func<TSource, TMember>> resolver)
        {
            var segments = new Stack<string>();
            Expression? current = resolver.Body;

            while (current is MemberExpression memberExpression)
            {
                segments.Push(memberExpression.Member.Name);
                current = memberExpression.Expression;
            }

            if (current is ParameterExpression && segments.Count > 0)
            {
                return string.Join(".", segments.ToArray());
            }

            return string.Empty;
        }
    }
}
