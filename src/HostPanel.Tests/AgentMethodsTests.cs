using HostPanel.Core.DTOs;

namespace HostPanel.Tests;

public class AgentMethodsTests
{
    [Fact]
    public void AllMethods_ContainsExpectedCount()
    {
        Assert.True(AgentMethods.AllMethods.Count >= 32, $"Expected at least 32 methods, found {AgentMethods.AllMethods.Count}");
    }

    [Theory]
    [InlineData("system.createUser")]
    [InlineData("nginx.createVhost")]
    [InlineData("backup.create")]
    [InlineData("firewall.addRule")]
    [InlineData("wordpress.install")]
    [InlineData("docker.create")]
    [InlineData("git.deploy")]
    [InlineData("security.clamScan")]
    [InlineData("app.configure")]
    [InlineData("ha.promoteToPrimary")]
    [InlineData("panel.update")]
    public void AllMethods_Contains(string method)
    {
        Assert.Contains(method, AgentMethods.AllMethods);
    }

    [Theory]
    [InlineData("dns.createZone")]
    [InlineData("mail.createMailbox")]
    public void AllMethods_ExcludesDnsAndMail(string method)
    {
        Assert.DoesNotContain(method, AgentMethods.AllMethods);
    }
}
