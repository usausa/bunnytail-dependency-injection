namespace BunnyTail.DependencyInjection.Internal;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

internal static class ParameterDefaults
{
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Only default values of value types are created, and the default constructor of a value type requires no metadata.")]
    public static bool TryGetDefaultValue(ParameterInfo parameter, out object? defaultValue)
    {
        defaultValue = null;
        if (!parameter.HasDefaultValue)
        {
            return false;
        }

        var value = parameter.DefaultValue;
        var parameterType = parameter.ParameterType;

        if (value is null)
        {
            if (parameterType.IsValueType && (Nullable.GetUnderlyingType(parameterType) is null))
            {
                defaultValue = Activator.CreateInstance(parameterType);
                return true;
            }

            defaultValue = null;
            return true;
        }

        if (parameterType.IsEnum && (value.GetType() != parameterType))
        {
            defaultValue = Enum.ToObject(parameterType, value);
            return true;
        }

        defaultValue = value;
        return true;
    }
}
