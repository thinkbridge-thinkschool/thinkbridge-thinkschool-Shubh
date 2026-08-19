BEGIN TRANSACTION;

UPDATE dbo.DeadlockTest
SET Balance = Balance + 100
WHERE Id = 1;

UPDATE dbo.DeadlockTest
SET Balance = Balance + 100
WHERE Id = 2;

COMMIT TRANSACTION;