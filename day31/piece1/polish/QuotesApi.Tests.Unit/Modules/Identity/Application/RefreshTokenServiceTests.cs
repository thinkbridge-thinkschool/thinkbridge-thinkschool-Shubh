using FluentAssertions;
using QuotesApi.Modules.Identity.Application;
using QuotesApi.Modules.Identity.Infrastructure.Clock;

namespace QuotesApi.Tests.Unit.Modules.Identity.Application;

public class RefreshTokenServiceTests
{
    private static RefreshTokenService CreateService(DateTimeOffset utcNow)
    {
        return new RefreshTokenService(new FakeClock { UtcNow = utcNow });
    }

    [Fact]
    public void IsReuseDetected_RevokedAndReplaced_ReturnsTrue()
    {
        var service = CreateService(DateTimeOffset.UtcNow);

        var result = service.IsReuseDetected(
            revokedAt: DateTimeOffset.UtcNow,
            replacedByToken: "some-hashed-replacement-token");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsReuseDetected_RevokedButNeverReplaced_ReturnsFalse()
    {
        var service = CreateService(DateTimeOffset.UtcNow);

        // A logout revokes a token without rotating it — that is not reuse.
        var result = service.IsReuseDetected(
            revokedAt: DateTimeOffset.UtcNow,
            replacedByToken: null);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsReuseDetected_NeverRevoked_ReturnsFalse()
    {
        var service = CreateService(DateTimeOffset.UtcNow);

        var result = service.IsReuseDetected(
            revokedAt: null,
            replacedByToken: null);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ExpiresAtInThePast_ReturnsTrue()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = CreateService(now);

        var result = service.IsExpired(now.AddMinutes(-1));

        result.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ExpiresAtExactlyNow_ReturnsTrue()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = CreateService(now);

        var result = service.IsExpired(now);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ExpiresAtInTheFuture_ReturnsFalse()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = CreateService(now);

        var result = service.IsExpired(now.AddDays(7));

        result.Should().BeFalse();
    }
}
