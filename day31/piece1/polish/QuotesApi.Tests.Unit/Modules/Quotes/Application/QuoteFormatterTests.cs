using FluentAssertions;
using QuotesApi.Modules.Quotes.Application;

namespace QuotesApi.Tests.Unit.Modules.Quotes.Application;

public class QuoteFormatterTests
{
    private readonly QuoteFormatter _formatter = new();

    [Fact]
    public void Format_TextWithLeadingAndTrailingWhitespace_TrimsIt()
    {
        var result = _formatter.Format("  Carpe diem.  ");

        result.Should().Be("Carpe diem.");
    }

    [Fact]
    public void Format_TextWithNoWhitespace_ReturnsUnchanged()
    {
        var result = _formatter.Format("Carpe diem.");

        result.Should().Be("Carpe diem.");
    }

    [Fact]
    public void Format_TextWithInternalWhitespace_PreservesInternalWhitespace()
    {
        var result = _formatter.Format("  Carpe   diem.  ");

        result.Should().Be("Carpe   diem.");
    }

    [Fact]
    public void Format_TextWithTabsAndNewlines_TrimsThemToo()
    {
        var result = _formatter.Format("\t\n Carpe diem. \n\t");

        result.Should().Be("Carpe diem.");
    }

    [Fact]
    public void Format_EmptyString_ReturnsEmptyString()
    {
        var result = _formatter.Format("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Format_WhitespaceOnlyString_ReturnsEmptyString()
    {
        var result = _formatter.Format("   ");

        result.Should().BeEmpty();
    }
}
