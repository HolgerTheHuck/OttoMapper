using System.Linq.Expressions;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Replaces a single <see cref="ParameterExpression"/> with a given <see cref="Expression"/> throughout
    /// an expression tree. Used to inline <c>MapFrom</c> resolver bodies and nested projection bodies into
    /// a parent projection. Inner lambda parameters (different <see cref="ParameterExpression"/> instances)
    /// are left untouched.
    /// </summary>
    internal sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _source;
        private readonly Expression _replacement;

        private ParameterReplacer(ParameterExpression source, Expression replacement)
        {
            _source = source;
            _replacement = replacement;
        }

        /// <summary>
        /// Returns a copy of <paramref name="body"/> with every occurrence of <paramref name="source"/>
        /// replaced by <paramref name="replacement"/>.
        /// </summary>
        public static Expression Replace(Expression body, ParameterExpression source, Expression replacement)
            => new ParameterReplacer(source, replacement).Visit(body);

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _source ? _replacement : node;
    }
}