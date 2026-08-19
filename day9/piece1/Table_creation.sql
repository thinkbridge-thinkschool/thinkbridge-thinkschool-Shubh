IF OBJECT_ID('dbo.IsolationTest', 'U') IS NOT NULL
    DROP TABLE dbo.IsolationTest;

CREATE TABLE dbo.IsolationTest
(
    Id INT PRIMARY KEY,
    Name NVARCHAR(100),
    Balance DECIMAL(10,2)
);

INSERT INTO dbo.IsolationTest (Id, Name, Balance)
VALUES
(1, 'Rahul', 1000.00),
(2, 'Priya', 2000.00),
(3, 'Amit', 3000.00);