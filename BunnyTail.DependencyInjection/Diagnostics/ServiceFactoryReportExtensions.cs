namespace BunnyTail.DependencyInjection.Diagnostics;

using System.Text;

public static class ServiceFactoryReportExtensions
{
    public static IReadOnlyList<ServiceFactoryReportEntry> CreateFactoryReport(this GeneratedServiceProvider provider)
    {
        return provider.Registry.CreateFactoryReport();
    }

    public static string DescribeRuntimeFallbacks(
        this GeneratedServiceProvider provider,
        Func<ServiceFactoryReportEntry, bool>? predicate = null,
        Func<ServiceFactoryReportEntry, string, string>? formatter = null)
    {
        var builder = new StringBuilder();
        var typeName = new StringBuilder();
        var written = new HashSet<Type>();
        foreach (var entry in provider.CreateFactoryReport())
        {
            if ((entry.Status != ServiceFactoryStatus.RuntimeFallback) ||
                !entry.CanGenerateFactory ||
                (entry.ImplementationType is null) ||
                ((predicate is not null) && !predicate(entry)) ||
                !written.Add(entry.ImplementationType))
            {
                continue;
            }

            _ = typeName.Clear();
            AppendTypeName(typeName, entry.ImplementationType);

            _ = formatter is null
                ? builder.Append("[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::").Append(typeName).AppendLine("))]")
                : builder.AppendLine(formatter(entry, typeName.ToString()));
        }

        return builder.ToString();
    }

    private static void AppendTypeName(StringBuilder builder, Type type)
    {
        if (type.IsArray)
        {
            var element = type;
            var suffix = new StringBuilder();
            while (element.IsArray && (element.GetElementType() is { } inner))
            {
                _ = suffix.Append('[').Append(',', element.GetArrayRank() - 1).Append(']');
                element = inner;
            }

            AppendTypeName(builder, element);
            _ = builder.Append(suffix);
            return;
        }

        var arguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
        AppendTypeName(builder, type.IsGenericType ? type.GetGenericTypeDefinition() : type, arguments, arguments.Length);
    }

    private static void AppendTypeName(StringBuilder builder, Type definition, Type[] arguments, int count)
    {
        var start = 0;
        if (definition.IsNested && (definition.DeclaringType is { } declaringType))
        {
            start = declaringType.GetGenericArguments().Length;
            AppendTypeName(builder, declaringType, arguments, start);
            _ = builder.Append('.');
        }
        else if (!String.IsNullOrEmpty(definition.Namespace))
        {
            _ = builder.Append(definition.Namespace).Append('.');
        }

        var name = definition.Name;
        var index = name.IndexOf('`', StringComparison.Ordinal);
        _ = builder.Append(index >= 0 ? name.AsSpan(0, index) : name.AsSpan());

        if (count <= start)
        {
            return;
        }

        _ = builder.Append('<');
        for (var i = start; i < count; i++)
        {
            if (i > start)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append("global::");
            AppendTypeName(builder, arguments[i]);
        }

        _ = builder.Append('>');
    }
}
