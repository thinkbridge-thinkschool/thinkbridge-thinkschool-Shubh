namespace QuotesApi.Modules.Quotes.Application;

public class QuoteFormatter : IQuoteFormatter
{
    public string Format(string text)
    {
        return text.Trim();
    }
}