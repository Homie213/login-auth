using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HeraxLicensing;

public sealed class GistLicenseClient
{
    private readonly HttpClient _httpClient;
    private readonly string _rawDatabaseUrl;

    public GistLicenseClient(HttpClient httpClient, string rawDatabaseUrl)
    {
        _httpClient = httpClient;
        _rawDatabaseUrl = rawDatabaseUrl;
    }

    public async Task<LicenseCheckResult> ValidateAsync(string username, string password, string licenseKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(licenseKey))
        {
            return LicenseCheckResult.Fail("Username, password, and license key are required.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(_rawDatabaseUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return LicenseCheckResult.Fail($"License server returned {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var database = await JsonSerializer.DeserializeAsync<LicenseDatabase>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (database is null)
            {
                return LicenseCheckResult.Fail("License database is empty.");
            }

            if (!database.Users.TryGetValue(username, out var user))
            {
                return LicenseCheckResult.Fail("Account not found.");
            }

            var passwordHash = ComputePasswordHash(username, password);
            if (!string.Equals(user.PasswordHash, passwordHash, StringComparison.Ordinal))
            {
                return LicenseCheckResult.Fail("Invalid username or password.");
            }

            if (!database.Licenses.TryGetValue(licenseKey, out var license))
            {
                return LicenseCheckResult.Fail("License key not found.");
            }

            if (!string.Equals(license.Username, username, StringComparison.Ordinal))
            {
                return LicenseCheckResult.Fail("This license does not belong to the current user.");
            }

            if (string.Equals(license.Status, "revoked", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseCheckResult.Fail("License has been revoked. Contact support.");
            }

            if (string.Equals(license.Status, "paused", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseCheckResult.Fail("License is paused. Contact support.");
            }

            if (!string.Equals(license.Expiry, "lifetime", StringComparison.OrdinalIgnoreCase) &&
                DateOnly.TryParse(license.Expiry, out var expiry) &&
                expiry < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return LicenseCheckResult.Fail("License has expired.");
            }

            return LicenseCheckResult.Success(license);
        }
        catch (HttpRequestException)
        {
            return LicenseCheckResult.Fail("Could not reach the license server.");
        }
        catch (JsonException)
        {
            return LicenseCheckResult.Fail("License data could not be parsed.");
        }
    }

    public static void SaveLocalAccount(string path, string username, string password)
    {
        var account = new LocalAccount(username, ComputePasswordHash(username, password));
        var json = JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static string ComputePasswordHash(string username, string password)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{username}|{password}|HeraxLicensing");
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }
}

public sealed record LocalAccount(string Username, string PasswordHash);

public sealed record LicenseDatabase(
    [property: JsonPropertyName("users")] Dictionary<string, StoredUser> Users,
    [property: JsonPropertyName("licenses")] Dictionary<string, StoredLicense> Licenses);

public sealed record StoredUser(
    [property: JsonPropertyName("password_hash")] string PasswordHash,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created")] string Created);

public sealed record StoredLicense(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expiry")] string Expiry,
    [property: JsonPropertyName("created")] string Created,
    [property: JsonPropertyName("revenue")] int Revenue,
    [property: JsonPropertyName("notes")] string Notes);

public sealed class LicenseCheckResult
{
    public bool IsValid { get; private init; }
    public string Message { get; private init; } = string.Empty;
    public StoredLicense? License { get; private init; }

    public static LicenseCheckResult Success(StoredLicense license) =>
        new() { IsValid = true, Message = "License is valid.", License = license };

    public static LicenseCheckResult Fail(string message) =>
        new() { IsValid = false, Message = message };
}
