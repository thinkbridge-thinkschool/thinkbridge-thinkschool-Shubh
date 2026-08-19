BEGIN TRANSACTION;

UPDATE dbo.DeadlockTest
SET Balance = Balance + 200
WHERE Id = 2;

WAITFOR DELAY '00:00:10';

UPDATE dbo.DeadlockTest
SET Balance = Balance + 200
WHERE Id = 1;

COMMIT TRANSACTION;