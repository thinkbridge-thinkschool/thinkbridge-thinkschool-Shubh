BEGIN TRANSACTION;

UPDATE dbo.DeadlockTest
SET Balance = Balance + 200
WHERE Id = 1;

UPDATE dbo.DeadlockTest
SET Balance = Balance + 200
WHERE Id = 2;

COMMIT TRANSACTION;