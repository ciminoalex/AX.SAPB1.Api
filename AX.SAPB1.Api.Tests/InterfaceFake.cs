using System.Reflection;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Finto minimo di un'interfaccia senza librerie di mock (il progetto di test non ne ha): ogni chiamata passa a
/// un gestore per nome del metodo. Un metodo non gestito lancia, così un test non passa per caso su un ramo che
/// non ha previsto.
/// </summary>
public class InterfaceFake<T> : DispatchProxy where T : class
{
    private Dictionary<string, Func<object?[], object?>> _handlers = new();

    public static T Create(Dictionary<string, Func<object?[], object?>> handlers)
    {
        var proxy = Create<T, InterfaceFake<T>>();
        ((InterfaceFake<T>)(object)proxy)._handlers = handlers;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod?.Name ?? string.Empty;
        if (_handlers.TryGetValue(name, out var handler)) return handler(args ?? Array.Empty<object?>());
        throw new NotSupportedException($"{typeof(T).Name}.{name} non previsto dal test.");
    }
}
