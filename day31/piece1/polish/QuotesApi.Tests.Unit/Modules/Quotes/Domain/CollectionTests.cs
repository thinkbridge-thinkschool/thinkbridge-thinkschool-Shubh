using FluentAssertions;
using QuotesApi.Modules.Quotes.Domain;

namespace QuotesApi.Tests.Unit.Modules.Quotes.Domain;

public class CollectionTests
{
    [Fact]
    public void Constructor_ValidName_CreatesCollectionOwnedByCaller()
    {
        var collection = new Collection("Favorites", ownerId: 1);

        collection.Name.Should().Be("Favorites");
        collection.OwnerId.Should().Be(1);
        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_TrimsName()
    {
        var collection = new Collection("  Favorites  ", ownerId: 1);

        collection.Name.Should().Be("Favorites");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public void Constructor_NameShorterThan3Characters_ThrowsArgumentException(string? name)
    {
        var act = () => new Collection(name!, ownerId: 1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Collection name must be between 3 and 80 characters.");
    }

    [Fact]
    public void Constructor_NameLongerThan80Characters_ThrowsArgumentException()
    {
        var tooLongName = new string('a', 81);

        var act = () => new Collection(tooLongName, ownerId: 1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Collection name must be between 3 and 80 characters.");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(80)]
    public void Constructor_NameAtBoundaryLength_IsAccepted(int length)
    {
        var name = new string('a', length);

        var collection = new Collection(name, ownerId: 1);

        collection.Name.Should().Be(name);
    }

    [Fact]
    public void AddItem_ValidQuoteId_AddsItemToCollection()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        var addedAt = DateTimeOffset.UtcNow;

        collection.AddItem(quoteId: 42, addedAt);

        collection.Items.Should().ContainSingle();
        collection.Items.Single().QuoteId.Should().Be(42);
        collection.Items.Single().AddedAt.Should().Be(addedAt.UtcDateTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddItem_NonPositiveQuoteId_ThrowsArgumentException(int quoteId)
    {
        var collection = new Collection("Favorites", ownerId: 1);

        var act = () => collection.AddItem(quoteId, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>()
            .WithMessage("QuoteId must be greater than zero.");
    }

    [Fact]
    public void AddItem_DuplicateQuoteId_ThrowsInvalidOperationException()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(quoteId: 1, DateTimeOffset.UtcNow);

        var act = () => collection.AddItem(quoteId: 1, DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Quote already exists in the collection.");
        collection.Items.Should().ContainSingle();
    }

    [Fact]
    public void AddItem_51stItem_ThrowsInvalidOperationException()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        for (var quoteId = 1; quoteId <= 50; quoteId++)
        {
            collection.AddItem(quoteId, DateTimeOffset.UtcNow);
        }

        var act = () => collection.AddItem(quoteId: 51, DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A collection can contain at most 50 items.");
        collection.Items.Should().HaveCount(50);
    }

    [Fact]
    public void AddItem_50thItem_IsAccepted()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        for (var quoteId = 1; quoteId < 50; quoteId++)
        {
            collection.AddItem(quoteId, DateTimeOffset.UtcNow);
        }

        var act = () => collection.AddItem(quoteId: 50, DateTimeOffset.UtcNow);

        act.Should().NotThrow();
        collection.Items.Should().HaveCount(50);
    }

    [Fact]
    public void RemoveItem_ExistingQuoteId_RemovesItem()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(quoteId: 1, DateTimeOffset.UtcNow);

        collection.RemoveItem(quoteId: 1);

        collection.Items.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_QuoteNotInCollection_ThrowsInvalidOperationException()
    {
        var collection = new Collection("Favorites", ownerId: 1);

        var act = () => collection.RemoveItem(quoteId: 999);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Quote does not exist in the collection.");
    }
}
