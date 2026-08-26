using GvrLicense.Domain.Audit;
using Xunit;

namespace GvrLicense.Api.Tests;

public class AuditActionDescriberTests
{
    [Theory]
    [InlineData("license.create", null, "Licencia creada")]
    [InlineData("device.kick", null, "PC liberado")]
    public void Describe_KnownActions(string action, string? details, string expected) =>
        Assert.Equal(expected, AuditActionDescriber.Describe(action, details));

    [Fact]
    public void Describe_StatusChange_ToSuspended() =>
        Assert.Equal(
            "Licencia suspendida",
            AuditActionDescriber.Describe("license_status_changed", """{"from":"Active","to":"Suspended"}"""));

    [Fact]
    public void Describe_StatusChange_ToActive() =>
        Assert.Equal(
            "Licencia reactivada",
            AuditActionDescriber.Describe("license_status_changed", """{"from":"Suspended","to":"Active"}"""));
}
