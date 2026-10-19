using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AipApi.Applications;

/// <summary>Application reference shown to the customer, e.g. AIP-20261019-7F3C2A9B.</summary>
public static partial class ApplicationId
{
    public const string Format = "AIP-yyyyMMdd-XXXXXXXX";

    public static string New(DateTime utcNow) =>
        $"AIP-{utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^AIP-[0-9]{8}-[0-9A-F]{8}$")]
    private static partial Regex Pattern();
}
