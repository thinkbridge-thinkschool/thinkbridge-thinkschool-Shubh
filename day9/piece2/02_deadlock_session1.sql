BEGIN TRANSACTION;

UPDATE dbo.DeadlockTest
SET Balance = Balance + 100
WHERE Id = 1;

WAITFOR DELAY '00:00:10';

UPDATE dbo.DeadlockTest
SET Balance = Balance + 100
WHERE Id = 2;

COMMIT TRANSACTION;