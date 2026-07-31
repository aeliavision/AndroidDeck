using System;
using FluentAssertions;
using VcfEditor.Core.Security;
using Xunit;

namespace VcfEditor.Core.Tests;

public sealed class PairingKeyExchangeTests
{
    [Fact]
    public void HkdfSha256MatchesRfc5869CaseOne()
    {
        var ikm = Convert.FromHexString(new string('0', 22 * 2).Replace("00", "0B"));
        var salt = Convert.FromHexString("000102030405060708090A0B0C");
        var info = Convert.FromHexString("F0F1F2F3F4F5F6F7F8F9");

        var actual = PairingKeyExchange.HkdfSha256(ikm, salt, info, 42);

        Convert.ToHexString(actual).Should().Be(
            "3CB25F25FAACD57A90434F64D0362F2A" +
            "2D2D0A90CF1A5A4C5DB02D56ECC4C5BF" +
            "34007208D5B887185865");
    }
}
