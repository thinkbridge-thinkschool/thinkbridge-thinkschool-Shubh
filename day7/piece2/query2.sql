WITH QuoteWindows AS
(
    SELECT
        Id,
        Author,
        Text,
        CreatedAt,

        COUNT(*) OVER
        (
            PARTITION BY Author
            ORDER BY CreatedAt
            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        ) AS RunningQuoteCount,

        LAG(CreatedAt) OVER
        (
            PARTITION BY Author
            ORDER BY CreatedAt
        ) AS PreviousQuoteDate

    FROM Quotes
    WHERE IsDeleted = 0
      AND COALESCE(TRIM(Author), '') <> ''
)
SELECT
    Id,
    Author,
    Text,
    CreatedAt,
    RunningQuoteCount,
    PreviousQuoteDate,
    CASE
        WHEN PreviousQuoteDate IS NULL THEN NULL
        ELSE CAST(
            julianday(CreatedAt) - julianday(PreviousQuoteDate)
            AS INTEGER
        )
    END AS GapInDays
FROM QuoteWindows
ORDER BY Author, CreatedAt;