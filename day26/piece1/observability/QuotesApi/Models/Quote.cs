namespace QuotesApi.Models;

public class Quote
{
    private Quote()
    {
    }

    private Quote(string author, string text, int userId)
    {
        Author = author;
        Text = text;
        UserId = userId;
    }

    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public int UserId { get; private set; }

    public static (Quote? Quote, QuoteDomainError? Error) Create(
        string author,
        string text,
        int userId)
    {
        var normalizedAuthor = author?.Trim();
        var normalizedText = text?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedAuthor) || normalizedAuthor.Length > 200)
            return (null, new QuoteDomainError("author", "Author must be between 1 and 200 characters."));

        if (string.IsNullOrWhiteSpace(normalizedText) || normalizedText.Length > 1000)
            return (null, new QuoteDomainError("text", "Text must be between 1 and 1000 characters."));

        return (new Quote(normalizedAuthor, normalizedText, userId), null);
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}

public sealed record QuoteDomainError(string PropertyName, string Message);