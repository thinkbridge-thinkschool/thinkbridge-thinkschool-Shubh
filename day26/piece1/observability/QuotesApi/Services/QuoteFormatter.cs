namespace QuotesApi.Services;

public class QuoteFormatter : IQuoteFormatter
{
    public string Format(string text)
    {
        return text.Trim();
    }
}