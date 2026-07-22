using System;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Thrown when a projection expression (<c>ProjectTo</c> / <c>BuildProjection</c>) cannot be built
    /// because the configured map uses customizations that are not translatable to an
    /// <see cref="System.Linq.IQueryable"/> provider (e.g. EF Core). The message names the affected member
    /// or map and a recommended action where possible.
    /// </summary>
    public class ProjectionException : InvalidOperationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectionException"/> class with a detail message.
        /// </summary>
        public ProjectionException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectionException"/> class with a detail message
        /// and a reference to a destination member name, if applicable.
        /// </summary>
        public ProjectionException(string message, string? memberName) : base(memberName == null ? message : $"{message} (member: '{memberName}')")
        {
            MemberName = memberName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectionException"/> class with a detail message
        /// and an inner exception.
        /// </summary>
        public ProjectionException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets the destination member name that triggered the projection failure, if known.
        /// </summary>
        public string? MemberName { get; }
    }
}