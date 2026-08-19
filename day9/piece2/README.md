# Day 9 — Reproduce and Resolve a Deadlock

## Objective

The goal of this exercise was to reproduce a classic two-resource deadlock using two SQL Server sessions, capture the deadlock victim message, and resolve the deadlock by using a consistent lock ordering.

---

## 1. Deadlock Setup

The test used the `DeadlockTest` table with two rows:

| Id | Name | Balance |
|---:|---|---:|
| 1 | Account A | 1000.00 |
| 2 | Account B | 2000.00 |

---

## 2. Deadlock Reproduction

### Session 1

    BEGIN TRANSACTION;

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 100
    WHERE Id = 1;

    WAITFOR DELAY '00:00:10';

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 100
    WHERE Id = 2;

    COMMIT TRANSACTION;

Session 1 first acquired a lock on Id 1 and then attempted to acquire a lock on Id 2.

### Session 2

    BEGIN TRANSACTION;

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 200
    WHERE Id = 2;

    WAITFOR DELAY '00:00:10';

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 200
    WHERE Id = 1;

    COMMIT TRANSACTION;

Session 2 first acquired a lock on Id 2 and then attempted to acquire a lock on Id 1.

### Deadlock Victim

SQL Server detected the circular wait and selected one transaction as the deadlock victim.

Observed error:

    Msg 1205, Level 13, State 72

    Transaction (Process ID 55) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.

---

## 3. Deadlock Fix

The deadlock was fixed by making both transactions acquire the resources in the same order.

### Fixed Session 1

    BEGIN TRANSACTION;

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 100
    WHERE Id = 1;

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 100
    WHERE Id = 2;

    COMMIT TRANSACTION;

### Fixed Session 2

    BEGIN TRANSACTION;

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 200
    WHERE Id = 1;

    UPDATE dbo.DeadlockTest
    SET Balance = Balance + 200
    WHERE Id = 2;

    COMMIT TRANSACTION;

### Result

Both fixed transactions completed successfully without a deadlock.

Both sessions reported:

    (1 row affected)
    (1 row affected)

No Msg 1205 error occurred.

### Why the Fix Works

Both transactions acquire locks in the same order (Id 1 → Id 2), so they cannot form the circular wait required for a deadlock.

---

## What Did I Learn?

I learned how deadlocks occur when two transactions hold different locks and wait for each other. I also learned how SQL Server detects a deadlock, chooses a victim transaction, and how consistent lock ordering can prevent deadlocks.

## What Would Break This?

Inconsistent lock ordering between transactions can recreate the deadlock. Long-running transactions and holding locks longer than necessary can also increase the chance of blocking and deadlocks.