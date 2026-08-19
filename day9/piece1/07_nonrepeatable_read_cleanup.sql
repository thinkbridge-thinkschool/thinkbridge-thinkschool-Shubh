COMMIT TRANSACTION;

UPDATE dbo.IsolationTest
SET Balance = 1000.00
WHERE Id = 1;

SELECT *
FROM dbo.IsolationTest
WHERE Id = 1;