using System.Text;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>An exact, source-domain-qualified reported identity. Values are never normalized.</summary>
public readonly record struct ReportedVehicleIdentity
{
    public const int MaxNamespaceBytes = 64;
    public const int MaxKindBytes = 64;
    public const int MaxValueBytes = 512;

    public string Namespace { get; }
    public string Kind { get; }
    public string Value { get; }

    ReportedVehicleIdentity(string identityNamespace, string kind, string value)
    {
        Namespace = identityNamespace;
        Kind = kind;
        Value = value;
    }

    public static ReportedVehicleIdentity Create(string identityNamespace, string kind, string value)
    {
        ValidateKey(identityNamespace, MaxNamespaceBytes, nameof(identityNamespace));
        ValidateKey(kind, MaxKindBytes, nameof(kind));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("An identity value must be nonblank.", nameof(value));
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("An identity value cannot contain NUL.", nameof(value));
        if (Encoding.UTF8.GetByteCount(value) > MaxValueBytes)
            throw new ArgumentOutOfRangeException(nameof(value), $"Identity values are limited to {MaxValueBytes} UTF-8 bytes.");
        return new ReportedVehicleIdentity(identityNamespace, kind, value);
    }

    public static bool TryCreate(string? identityNamespace, string? kind, string? value,
        out ReportedVehicleIdentity identity, out ReportedVehicleIdentityProblem problem)
    {
        identity = default;
        problem = ReportedVehicleIdentityProblem.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            problem = ReportedVehicleIdentityProblem.Missing;
            return false;
        }
        try
        {
            identity = Create(identityNamespace ?? string.Empty, kind ?? string.Empty, value);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            problem = ReportedVehicleIdentityProblem.LimitExceeded;
            return false;
        }
        catch (ArgumentException)
        {
            problem = ReportedVehicleIdentityProblem.Invalid;
            return false;
        }
    }

    static void ValidateKey(string value, int maxBytes, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(value) > maxBytes)
            throw new ArgumentException($"{name} must be nonblank, contain no NUL, and fit within {maxBytes} UTF-8 bytes.", name);
    }
}

public enum ReportedVehicleIdentityProblem { None, Missing, Invalid, LimitExceeded }
