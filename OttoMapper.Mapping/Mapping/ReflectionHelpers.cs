using System;
using System.Reflection;

namespace OttoMapper.Mapping
{
    internal static class ReflectionHelpers
    {
        public static MethodInfo GetRequiredMethod(Type declaringType, string name, BindingFlags bindingFlags)
        {
            var method = declaringType.GetMethod(name, bindingFlags);
            if (method == null)
            {
                throw new InvalidOperationException($"Required method '{declaringType.FullName}.{name}' could not be found.");
            }

            return method;
        }
    }
}
