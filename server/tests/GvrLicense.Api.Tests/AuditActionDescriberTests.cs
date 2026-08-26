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

    // El trigger audit_license_status_change de Postgres escribe el status como número (0/1/2),
    // no como texto -- este es el shape real que llega desde la base de datos.
    [Fact]
    public void Describe_StatusChange_NumericFromTrigger() =>
        Assert.Equal(
            "Licencia suspendida",
            AuditActionDescriber.Describe("license_status_changed", """{"from":0,"to":1}"""));
}
