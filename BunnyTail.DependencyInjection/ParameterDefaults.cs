namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

// コンストラクタ引数の既定値取得 (リフレクション表現の揺れを吸収する)
// Default value extraction for constructor parameters, absorbing quirks of the reflection representation.
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
                // struct の default はリフレクションが null を返すことがある
                // Reflection may report null for the default value of a struct.
                defaultValue = Activator.CreateInstance(parameterType);
                return true;
            }

            defaultValue = null;
            return true;
        }

        // enum の既定値は underlying 型で返ることがある
        // Enum defaults may be reported as the underlying type.
        if (parameterType.IsEnum && value.GetType() != parameterType)
        {
            defaultValue = Enum.ToObject(parameterType, value);
            return true;
        }

        defaultValue = value;
        return true;
    }
}
