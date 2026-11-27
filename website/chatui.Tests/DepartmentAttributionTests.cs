using chatui.Configuration;
using Xunit;

namespace chatui.Tests;

public sealed class DepartmentAttributionTests
{
    private const string FinanceGroup = "11111111-1111-1111-1111-111111111111";
    private const string HrGroup = "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData(FinanceGroup, "finance")]
    [InlineData(HrGroup, "hr")]
    public void ResolvesOnlyConfiguredGroup(string group, string expected)
    {
        Assert.Equal(expected, Options().Resolve([group, "other-group"]).Key);
    }

    [Fact]
    public void MissingUnknownAndAmbiguousMembershipAreRejected()
    {
        Assert.Throws<DepartmentAccessDeniedException>(() => Options().Resolve([]));
        Assert.Throws<DepartmentAccessDeniedException>(() => Options().Resolve(["finance"]));
        Assert.Throws<DepartmentAccessDeniedException>(() => Options().Resolve([FinanceGroup, HrGroup]));
    }

    [Fact]
    public void DisabledAccessCannotResolveDepartment()
    {
        Assert.Throws<InvalidOperationException>(() => new DepartmentAccessOptions().Resolve([FinanceGroup]));
    }

    [Fact]
    public void EnabledAccessRequiresCompleteConfiguration()
    {
        Assert.False(new DepartmentAccessOptions { Enabled = true }.IsValid());
        Assert.True(Options().IsValid());
    }

    private static DepartmentAccessOptions Options() => new()
    {
        Enabled = true,
        Departments = new()
        {
            ["finance"] = new() { GroupObjectId = FinanceGroup, SubscriptionId = "finance-sub", KeySecretName = "finance-key" },
            ["hr"] = new() { GroupObjectId = HrGroup, SubscriptionId = "hr-sub", KeySecretName = "hr-key" },
        },
    };
}