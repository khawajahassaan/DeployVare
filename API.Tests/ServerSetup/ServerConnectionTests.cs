using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using API.DTOs.Requests;
using API.Services.Implementations;
using Renci.SshNet.Common;

namespace API.Tests.ServerSetup;

public class ServerConnectionTests
{
    private static readonly RegularExpressionAttribute HostRule = new(ServerHostName.Pattern);

    [Theory]
    [InlineData("20.203.78.63")]
    [InlineData("server.domain.com")]
    [InlineData("localhost")]
    [InlineData("WIN-HOST01")]
    [InlineData("fe80::1")]
    [InlineData("::1")]
    public void HostName_AcceptsAddressesAndNames(string host)
    {
        Assert.True(HostRule.IsValid(host));
    }

    [Theory]
    [InlineData("20.203.78.63:55000")]
    [InlineData("localhost:22")]
    [InlineData("ssh://server.domain.com")]
    [InlineData("server name")]
    public void HostName_RejectsPortsUrlsAndSpaces(string host)
    {
        Assert.False(HostRule.IsValid(host));
    }

    [Fact]
    public void HostWithPort_ExplainsThatThePortHasItsOwnField()
    {
        var message = SshService.DescribeConnectionFailure(
            new SocketException((int)SocketError.HostNotFound), "20.203.78.63:55000", 22, "admins2");

        Assert.Contains("could not be found", message);
        Assert.Contains("SSH Port", message);
    }

    [Fact]
    public void RefusedConnection_SaysNothingIsListening()
    {
        var message = SshService.DescribeConnectionFailure(
            new SocketException((int)SocketError.ConnectionRefused), "20.203.78.63", 22, "admins2");

        Assert.Contains("refused", message);
    }

    [Fact]
    public void Timeout_PointsAtFirewallRules()
    {
        var message = SshService.DescribeConnectionFailure(
            new SocketException((int)SocketError.TimedOut), "20.203.78.63", 22, "admins2");

        Assert.Contains("firewall", message);
    }

    [Fact]
    public void NoSshResponse_MentionsThePortMayNotBeSsh()
    {
        var message = SshService.DescribeConnectionFailure(
            new SshOperationTimeoutException("Socket read operation has timed out"), "20.203.78.63", 55000, "admins2");

        Assert.Contains("not SSH", message);
    }

    [Fact]
    public void RejectedLogin_NamesTheUser()
    {
        var message = SshService.DescribeConnectionFailure(
            new SshAuthenticationException("Permission denied (password)."), "20.203.78.63", 22, @".\admins2");

        Assert.Contains("rejected the login", message);
        Assert.Contains(@".\admins2", message);
    }
}
