namespace AtomPix.Core.ValueObjects;

public readonly record struct LocalPath
{
    public LocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Local path cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}