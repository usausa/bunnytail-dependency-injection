namespace BunnyTail.Resolver;

using System.Text;

// MEDI の TypeNameHelper 相当 (例外メッセージ用の型名表示。generics は <...> に展開)
// Equivalent of MEDI's TypeNameHelper (type name display for exception messages, generics expanded as <...>).
internal static class TypeNameHelper
{
    public static string GetTypeDisplayName(Type type)
    {
        var builder = new StringBuilder();
        Append(builder, type);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, Type type)
    {
        if (type.IsGenericType)
        {
            var name = type.FullName is not null && !type.IsConstructedGenericType ? type.FullName : GetFullNameWithoutArity(type);
            builder.Append(name);
            builder.Append('<');
            var arguments = type.GetGenericArguments();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                Append(builder, arguments[i]);
            }

            builder.Append('>');
        }
        else if (type.IsArray)
        {
            Append(builder, type.GetElementType()!);
            builder.Append("[]");
        }
        else
        {
            builder.Append(type.FullName ?? type.Name);
        }
    }

    private static string GetFullNameWithoutArity(Type type)
    {
        var name = type.IsNested ? type.DeclaringType!.FullName + "+" + type.Name : type.Namespace + "." + type.Name;
        var index = name.IndexOf('`', StringComparison.Ordinal);
        return index >= 0 ? name[..index] : name;
    }
}
