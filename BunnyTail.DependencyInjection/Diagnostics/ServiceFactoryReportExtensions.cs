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
        if (type.IsNested && (type.DeclaringType is not null))
        {
            AppendTypeName(builder, type.DeclaringType);
            _ = builder.Append('.');
        }
        else if (!String.IsNullOrEmpty(type.Namespace))
        {
            _ = builder.Append(type.Namespace).Append('.');
        }

        var name = type.Name;
        var index = name.IndexOf('`', StringComparison.Ordinal);
        _ = builder.Append(index >= 0 ? name.AsSpan(0, index) : name.AsSpan());

        if (!type.IsGenericType)
        {
            return;
        }

        var arguments = type.GetGenericArguments();
        _ = builder.Append('<');
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append("global::");
            AppendTypeName(builder, arguments[i]);
        }

        _ = builder.Append('>');
    }
}
