namespace AtomPix.Desktop.Localization;

public static class LocalizationHost
{
    private static IAtomPixLocalizedText? _current;

    public static IAtomPixLocalizedText? Current => Volatile.Read(ref _current);

    public static void Initialize(IAtomPixLocalizedText service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Volatile.Write(ref _current, service);
    }

    public static void Release(IAtomPixLocalizedText service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Interlocked.CompareExchange(ref _current, null, service);
    }
}
