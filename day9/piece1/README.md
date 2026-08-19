# Day 9 — Isolation Levels + Read Anomalies

## Objective

The goal of this exercise was to reproduce three common read anomalies using two SQL Server sessions:

- Dirty Read
- Non-Repeatable Read
- Phantom Read

I also tested stronger isolation levels to determine which isolation level prevents each anomaly.

## Test Table

The experiments used the `IsolationTest` table.

Initial data:

| Id | Name | Balance |
|---:|---|---:|
| 1 | Rahul | 1000.00 |
| 2 | Priya | 2000.00 |
| 3 | Amit | 3000.00 |

---

## 1. Dirty Read

### Session 1

    BEGIN TRANSACTION;

    UPDATE dbo.IsolationTest
    SET Balance = 5000.00
    WHERE Id = 1;

The transaction was left uncommitted.

### Session 2

    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    SELECT
        Id,
        Name,
        Balance
    FROM dbo.IsolationTest
    WHERE Id = 1;

### Result

Session 2 read:

| Id | Name | Balance |
|---:|---|---:|
| 1 | Rahul | 5000.00 |

Session 2 was able to read the uncommitted value from Session 1.

This demonstrated a **dirty read**.

The transaction was then rolled back and the balance returned to 1000.00.

---

## 2. Non-Repeatable Read

### Session 1

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    BEGIN TRANSACTION;

    SELECT
        Id,
        Name,
        Balance
    FROM dbo.IsolationTest
    WHERE Id = 1;

### First Result

| Id | Name | Balance |
|---:|---|---:|
| 1 | Rahul | 1000.00 |

### Session 2

    UPDATE dbo.IsolationTest
    SET Balance = 7000.00
    WHERE Id = 1;

The update was committed.

### Session 1 — Second Read

    SELECT
        Id,
        Name,
        Balance
    FROM dbo.IsolationTest
    WHERE Id = 1;

### Second Result

| Id | Name | Balance |
|---:|---|---:|
| 1 | Rahul | 7000.00 |

The same transaction read the same row twice and received different values.

This demonstrated a **non-repeatable read**.

---

## 3. Phantom Read

### Session 1

    SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

    BEGIN TRANSACTION;

    SELECT
        Id,
        Name,
        Balance
    FROM dbo.IsolationTest
    WHERE Balance >= 2000;

### First Result

| Id | Name | Balance |
|---:|---|---:|
| 2 | Priya | 2000.00 |
| 3 | Amit | 3000.00 |

### Session 2

    INSERT INTO dbo.IsolationTest (Id, Name, Balance)
    VALUES (4, 'Neha', 2500.00);

### Session 1 — Second Read

    SELECT
        Id,
        Name,
        Balance
    FROM dbo.IsolationTest
    WHERE Balance >= 2000;

### Second Result

| Id | Name | Balance |
|---:|---|---:|
| 2 | Priya | 2000.00 |
| 3 | Amit | 3000.00 |
| 4 | Neha | 2500.00 |

The new Neha row appeared in the second read.

This demonstrated a **phantom read**.

---

## 4. Preventing Phantom Reads with SERIALIZABLE

### Session 1

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    BEGIN TRANSACTION;

    SELECT
        Id,
        Name,
        Balance
    FROM dbo.IsolationTest
    WHERE Balance >= 2000;

Session 1 returned the two existing matching rows.

### Session 2

    INSERT INTO dbo.IsolationTest (Id, Name, Balance)
    VALUES (4, 'Neha', 2500.00);

The INSERT was blocked while the SERIALIZABLE transaction was open.

This demonstrated that `SERIALIZABLE` prevents the phantom-producing insert.

---

## Isolation Level Summary

| Anomaly | Lowest Isolation Level That Prevents It |
|---|---|
| Dirty Read | **READ COMMITTED** |
| Non-Repeatable Read | **REPEATABLE READ** |
| Phantom Read | **SERIALIZABLE** |

---

## What Did I Learn?

I learned how SQL Server transaction isolation levels control what one transaction can see from another transaction. I reproduced dirty reads, non-repeatable reads, and phantom reads using two sessions and observed how stronger isolation levels prevent these anomalies.

## What Would Break This?

Using a weaker isolation level can allow read anomalies. Poor transaction management, long-running transactions, or excessive use of high isolation levels can also increase blocking and reduce concurrency.