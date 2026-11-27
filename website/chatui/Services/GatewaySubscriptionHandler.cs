using chatui.Auth;
using Microsoft.Extensions.Options;

namespace chatui.Services;

public sealed class GatewaySubscriptionHandler(
    GatewayDepartmentContext department,
    IOptions<GatewayDepartmentOptions> options,
    Uri gatewayEndpoint) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("Ocp-Apim-Subscription-Key");
        var target = request.RequestUri;
        if (target is not null && target.Scheme == Uri.UriSchemeHttps
            && target.Authority.Equals(gatewayEndpoint.Authority, StringComparison.OrdinalIgnoreCase)
            && target.AbsolutePath.StartsWith(gatewayEndpoint.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal)
            && options.Value.Enabled)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", department.IsFinance
                ? options.Value.FinanceSubscriptionKey : options.Value.UnallocatedSubscriptionKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}