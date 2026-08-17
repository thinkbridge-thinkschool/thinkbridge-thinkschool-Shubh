-- 1. Authors with quotes but no tags
-- EXCEPT: removes authors who also have tagged quotes

SELECT DISTINCT Author
FROM Quotes
WHERE IsDeleted = 0
  AND COALESCE(TRIM(Author), '') <> ''

EXCEPT

SELECT DISTINCT q.Author
FROM Quotes q
JOIN Tags t ON t.QuoteId = q.Id
WHERE q.IsDeleted = 0
  AND COALESCE(TRIM(q.Author), '') <> '';


-- 2. Authors in both classic and modern
-- INTERSECT: returns only authors present in both sets

SELECT DISTINCT q.Author
FROM Quotes q
JOIN Tags t ON t.QuoteId = q.Id
WHERE q.IsDeleted = 0
  AND t.Tag = 'classic'
  AND COALESCE(TRIM(q.Author), '') <> ''

INTERSECT

SELECT DISTINCT q.Author
FROM Quotes q
JOIN Tags t ON t.QuoteId = q.Id
WHERE q.IsDeleted = 0
  AND t.Tag = 'modern'
  AND COALESCE(TRIM(q.Author), '') <> '';


-- 3. Combined distinct tag list
-- UNION: combines both sets and removes duplicates

SELECT Tag
FROM Tags
WHERE Tag = 'classic'
UNION
SELECT Tag
FROM Tags
WHERE Tag = 'modern';