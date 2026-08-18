-- 1. Baseline query: before indexes
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
WHERE CustomerName = 'Rahul Sharma';

SET STATISTICS IO OFF;


-- 2. Clustered index
CREATE CLUSTERED INDEX IX_Orders_OrderId
ON Orders(OrderId);


-- 3. Non-clustered index on CustomerName
CREATE NONCLUSTERED INDEX IX_Orders_CustomerName
ON Orders(CustomerName)
INCLUDE
(
    ProductName,
    Category,
    City,
    OrderAmount,
    OrderDate
);


-- 4. Non-clustered index on City + OrderDate
CREATE NONCLUSTERED INDEX IX_Orders_City_OrderDate
ON Orders(City, OrderDate)
INCLUDE
(
    CustomerName,
    ProductName,
    OrderAmount
);