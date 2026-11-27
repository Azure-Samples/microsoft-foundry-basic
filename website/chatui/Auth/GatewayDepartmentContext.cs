using System.Text.Json;
using Microsoft.Extensions.Options;

namespace chatui.Auth;

public sealed class GatewayDepartmentOptions
{
    public const string SectionName = "GatewayDepartment";
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string FinanceRole { get; set; } = "Department.Finance";
    public string FinanceSubscriptionKey { get; set; } = string.Empty;
    public string UnallocatedSubscriptionKey { get; set; } = string.Empty;
}

public sealed class GatewayDepartmentContext(
    IHttpContextAccessor accessor,
    IOptions<GatewayDepartmentOptions> options,
    IHostEnvironment environment,
    IOptions<DevAuthOptions> devAuth)
{
    public bool IsFinance => options.Value.Enabled
        && !(environment.IsDevelopment() && devAuth.Value.Enabled)
        && IsFinancePrincipal(accessor.HttpContext, options.Value.TenantId, options.Value.FinanceRole);

    internal static bool IsFinancePrincipal(HttpContext? context, string tenantId, string financeRole)
    {
        var encoded = context?.Request.Headers["X-MS-CLIENT-PRINCIPAL"].ToString();
        if (string.IsNullOrWhiteSpace(encoded) || !Guid.TryParse(tenantId, out var expectedTenant))
            return false;

        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            var root = document.RootElement;
            if (root.GetProperty("auth_typ").GetString() != "aad")
                return false;

            var claims = root.GetProperty("claims").EnumerateArray()
                .Select(claim => (Type: claim.GetProperty("typ").GetString(), Value: claim.GetProperty("val").GetString()))
                .ToArray();
            var tenants = claims.Where(claim => claim.Type is "tid" or "http://schemas.microsoft.com/identity/claims/tenantid").ToArray();
            var identities = claims.Where(claim => claim.Type is "oid" or "http://schemas.microsoft.com/identity/claims/objectidentifier").ToArray();
            var roles = claims.Where(claim => claim.Type is "roles" or "role" or "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                .Select(claim => claim.Value)
                .Where(value => value?.StartsWith("Department.", StringComparison.Ordinal) == true)
                .Distinct(StringComparer.Ordinal).ToArray();

            return tenants.Length > 0
                && tenants.All(claim => Guid.TryParse(claim.Value, out var actualTenant) && actualTenant == expectedTenant)
                && identities.Length > 0
                && identities.All(claim => Guid.TryParse(claim.Value, out _))
                && roles.Length == 1 && roles[0] == financeRole;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}