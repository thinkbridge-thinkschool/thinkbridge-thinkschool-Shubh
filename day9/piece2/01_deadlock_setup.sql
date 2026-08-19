IF OBJECT_ID('dbo.DeadlockTest', 'U') IS NOT NULL
    DROP TABLE dbo.DeadlockTest;

CREATE TABLE dbo.DeadlockTest
(
    Id INT PRIMARY KEY,
    Name NVARCHAR(100),
    Balance DECIMAL(10,2)
);

INSERT INTO dbo.DeadlockTest (Id, Name, Balance)
VALUES
(1, 'Account A', 1000.00),
(2, 'Account B', 2000.00);

SELECT *
FROM dbo.DeadlockTest;