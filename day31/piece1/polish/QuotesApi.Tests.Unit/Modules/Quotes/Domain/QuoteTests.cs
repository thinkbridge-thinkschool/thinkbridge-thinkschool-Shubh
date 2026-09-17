using FluentAssertions;
using QuotesApi.Modules.Quotes.Domain;

namespace QuotesApi.Tests.Unit.Modules.Quotes.Domain;

public class QuoteTests
{
    [Fact]
    public void Create_ValidAuthorAndText_ReturnsQuoteWithNoError()
    {
        var (quote, error) = Quote.Create("Marcus Aurelius", "You have power over your mind.", 1);

        error.Should().BeNull();
        quote.Should().NotBeNull();
        quote!.Author.Should().Be("Marcus Aurelius");
        quote.Text.Should().Be("You have power over your mind.");
        quote.UserId.Should().Be(1);
        quote.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsWhitespaceFromAuthorAndText()
    {
        var (quote, error) = Quote.Create("  Seneca  ", "  Luck is what happens when preparation meets opportunity.  ", 1);

        error.Should().BeNull();
        quote!.Author.Should().Be("Seneca");
        quote.Text.Should().Be("Luck is what happens when preparation meets opportunity.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_MissingAuthor_ReturnsAuthorError(string? author)
    {
        var (quote, error) = Quote.Create(author!, "Some quote text", 1);

        quote.Should().BeNull();
        error.Should().NotBeNull();
        error!.PropertyName.Should().Be("author");
    }

    [Fact]
    public void Create_AuthorLongerThan200Characters_ReturnsAuthorError()
    {
        var tooLongAuthor = new string('A', 201);

        var (quote, error) = Quote.Create(tooLongAuthor, "Some quote text", 1);

        quote.Should().BeNull();
        error!.PropertyName.Should().Be("author");
    }

    [Fact]
    public void Create_AuthorExactly200Characters_IsAccepted()
    {
        var maxLengthAuthor = new string('A', 200);

        var (quote, error) = Quote.Create(maxLengthAuthor, "Some quote text", 1);

        error.Should().BeNull();
        quote!.Author.Should().Be(maxLengthAuthor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_MissingText_ReturnsTextError(string? text)
    {
        var (quote, error) = Quote.Create("Author", text!, 1);

        quote.Should().BeNull();
        error.Should().NotBeNull();
        error!.PropertyName.Should().Be("text");
    }

    [Fact]
    public void Create_TextLongerThan1000Characters_ReturnsTextError()
    {
        var tooLongText = new string('B', 1001);

        var (quote, error) = Quote.Create("Author", tooLongText, 1);

        quote.Should().BeNull();
        error!.PropertyName.Should().Be("text");
    }

    [Fact]
    public void Create_TextExactly1000Characters_IsAccepted()
    {
        var maxLengthText = new string('B', 1000);

        var (quote, error) = Quote.Create("Author", maxLengthText, 1);

        error.Should().BeNull();
        quote!.Text.Should().Be(maxLengthText);
    }

    [Fact]
    public void SoftDelete_MarksQuoteAsDeleted()
    {
        var (quote, _) = Quote.Create("Author", "Text", 1);

        quote!.SoftDelete();

        quote.IsDeleted.Should().BeTrue();
    }
}
