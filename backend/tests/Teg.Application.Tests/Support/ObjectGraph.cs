using System.Collections;
using System.Reflection;

namespace Teg.Application.Tests.Support;

/// <summary>Walks everything reachable from an object through its public properties (and the items of its collections), to check what a view can show.</summary>
internal static class ObjectGraph
{
    /// <summary>Every object reachable from <paramref name="root"/> (itself included), except primitives, strings, GUIDs and enum values.</summary>
    public static IReadOnlyList<object> Walk(object root)
    {
        var found = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<object>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var type = current.GetType();
            if (type.IsPrimitive || type.IsEnum || current is string or Guid or decimal)
            {
                continue;
            }

            if (!type.IsValueType && !seen.Add(current))
            {
                continue;
            }

            found.Add(current);

            if (current is IEnumerable items)
            {
                foreach (var item in items.Cast<object?>().OfType<object>())
                {
                    pending.Enqueue(item);
                }

                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                if (property.GetValue(current) is { } value)
                {
                    pending.Enqueue(value);
                }
            }
        }

        return found;
    }
}
