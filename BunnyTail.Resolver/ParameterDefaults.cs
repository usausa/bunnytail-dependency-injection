namespace BunnyTail.Resolver;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

internal static class ParameterDefaults
{
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "値型の default 値の生成のみ (値型の既定コンストラクタはメタデータ不要)")]
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
            if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null)
            {
                // struct の default (リフレクションは null を返すことがある)
                defaultValue = Activator.CreateInstance(parameterType);
                return true;
            }

            defaultValue = null;
            return true;
        }

        // enum の既定値は underlying 型で返ることがある
        if (parameterType.IsEnum && value.GetType() != parameterType)
        {
            defaultValue = Enum.ToObject(parameterType, value);
            return true;
        }

        defaultValue = value;
        return true;
    }
}
