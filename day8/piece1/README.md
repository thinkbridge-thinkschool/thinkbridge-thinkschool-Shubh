# Day 8 — Clustered vs Non-Clustered Indexes

## Dataset

Created an Orders table with 100,000 realistic rows.

## Indexes

### Clustered Index
IX_Orders_OrderId on OrderId

### Non-Clustered Index 1
IX_Orders_CustomerName on CustomerName
with included columns:
ProductName, Category, City, OrderAmount, OrderDate

### Non-Clustered Index 2
IX_Orders_City_OrderDate on City, OrderDate
with included columns:
CustomerName, ProductName, OrderAmount

## Logical Reads

| Test | Before | After |
|---|---:|---:|
| Clustered index | 1,695 | 1,544 |
| CustomerName index | 1,544 | 77 |
| City + OrderDate index | 1,544 | 52 |

## Write-side Cost

Indexes improve read performance but add storage and write overhead because INSERT, UPDATE, and DELETE operations must maintain the indexes.

## What I learned

I learned how clustered and non-clustered indexes affect logical reads and how a covering index can significantly reduce reads for a query.

## What would break this?

Too many indexes, frequent writes, low-selectivity columns, or queries that do not match the index key can reduce the benefit and increase write/storage overhead.