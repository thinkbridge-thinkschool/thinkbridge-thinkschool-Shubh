WITH RankedQuotes AS
(
    SELECT
        Id,
        Author,
        Text,
        ROW_NUMBER() OVER
        (
            PARTITION BY Author
            ORDER BY Id DESC
        ) AS rn
    FROM Quotes
    WHERE IsDeleted = 0
),
QuoteCounts AS
(
    SELECT
        Author,
        COUNT(*) AS QuoteCount
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT
    qc.Author,
    qc.QuoteCount,
    rq.Text AS MostRecentQuote
FROM QuoteCounts qc
LEFT JOIN RankedQuotes rq
    ON rq.Author = qc.Author
   AND rq.rn = 1
ORDER BY qc.Author;