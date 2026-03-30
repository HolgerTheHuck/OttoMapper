using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Represents a configured destination path mapping.
    /// </summary>
    public class PathMap
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PathMap"/> class.
        /// </summary>
        /// <param name="path">The configured destination path.</param>
        public PathMap(string path)
        {
            Path = path;
        }

        /// <summary>
        /// Gets the configured destination path.
        /// </summary>
        public string Path { get; }

        internal Func<object, object>? Resolver { get; set; }

        internal Func<object, bool>? Condition { get; set; }

        internal Func<object, object, bool>? ConditionWithDestination { get; set; }

        internal object? NullSubstitute { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the path is ignored.
        /// </summary>
        public bool Ignore { get; set; }

        /// <summary>
        /// Extracts a dot-separated member path from a destination expression.
        /// </summary>
        /// <param name="expression">The destination member expression.</param>
        /// <returns>The resolved member path.</returns>
        public static string GetPath(LambdaExpression expression)
        {
            var segments = new Stack<string>();
            Expression? current = expression.Body;

            while (current is MemberExpression memberExpression)
            {
                segments.Push(memberExpression.Member.Name);
                current = memberExpression.Expression;
            }

            if (segments.Count == 0)
            {
                throw new ArgumentException("Destination path must be a member access.", nameof(expression));
            }

            return string.Join(".", segments.ToArray());
        }
    }
}
