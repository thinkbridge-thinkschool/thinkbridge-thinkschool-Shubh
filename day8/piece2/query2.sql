-- Day 8 Piece 2
-- Covering Index + INCLUDE

-- BEFORE: non-covering index
CREATE NONCLUSTERED INDEX IX_Orders_CustomerName_CoveringTest
ON Orders(CustomerName);

-- Query that produced the Key Lookup
SET STATISTICS IO ON;

SELECT
    OrderId,
    CustomerName,
    ProductName,
    Category,
    City,
    OrderAmount,
    OrderDate
FROM Orders
WHERE CustomerName = 'Index Test User';

SET STATISTICS IO OFF;


-- Replace the non-covering index with a covering index
DROP INDEX IX_Orders_CustomerName_CoveringTest
ON Orders;

CREATE NONCLUSTERED INDEX IX_Orders_CustomerName_CoveringTest
ON Orders(CustomerName)
INCLUDE
(
    ProductName,
    Category,
    City,
    OrderAmount,
    OrderDate
);


-- AFTER: same query
SET STATISTICS IO ON;

SELECT
    OrderId,
    CustomerName,
    ProductName,
    Category,
    City,
    OrderAmount,
    OrderDate
FROM Orders
WHERE CustomerName = 'Index Test User';

SET STATISTICS IO OFF;