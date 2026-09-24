-- 010: new-item requests move through stages, and every step is recorded.
--
--   AwaitingDeptHead -> AwaitingBudget -> Procuring -> Arrived
--   (Rejected at either of the first two; Cancelled kept for old rows)
--
-- 1. Adds NewItemRequestSteps: one row per step -- the stage the request
--    moved into, who moved it, when, and their remarks.
-- 2. Writes the steps existing requests already went through (submitted,
--    and approved / rejected / fulfilled where that happened).
-- 3. Renames the old statuses: Pending -> AwaitingDeptHead,
--    Approved -> Procuring (they were approved under the one-step process,
--    so they carry on from procurement), Fulfilled -> Arrived.
-- 4. Replaces the status check constraint and default.
--
-- Run with the app stopped, after backing up. Safe to run twice.

IF OBJECT_ID('dbo.NewItemRequestSteps') IS NULL
BEGIN
    CREATE TABLE dbo.NewItemRequestSteps (
        StepID            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NewItemRequestSteps PRIMARY KEY,
        NewItemRequestID  INT          NOT NULL,
        Status            VARCHAR(20)  NOT NULL,
        ActedByUserID     INT          NOT NULL,
        ActedAt           DATETIME     NOT NULL CONSTRAINT DF_NewItemRequestSteps_ActedAt DEFAULT (GETDATE()),
        Remarks           NVARCHAR(500) NULL,
        CONSTRAINT FK_NewItemRequestSteps_Request FOREIGN KEY (NewItemRequestID)
            REFERENCES dbo.NewItemRequests (NewItemRequestID) ON DELETE CASCADE,
        CONSTRAINT FK_NewItemRequestSteps_User FOREIGN KEY (ActedByUserID)
            REFERENCES dbo.Users (UserID)
    );

    CREATE INDEX IX_NewItemRequestSteps_Request ON dbo.NewItemRequestSteps (NewItemRequestID, ActedAt);

    -- Every existing request was submitted by its requester.
    INSERT INTO dbo.NewItemRequestSteps (NewItemRequestID, Status, ActedByUserID, ActedAt, Remarks)
    SELECT NewItemRequestID, 'AwaitingDeptHead', RequestedByUserID, RequestDate, NULL
    FROM dbo.NewItemRequests;

    -- And the decision taken on it, if any.
    INSERT INTO dbo.NewItemRequestSteps (NewItemRequestID, Status, ActedByUserID, ActedAt, Remarks)
    SELECT NewItemRequestID,
           CASE RequestStatus WHEN 'Approved'  THEN 'Procuring'
                              WHEN 'Fulfilled' THEN 'Arrived'
                              ELSE RequestStatus END,
           ReviewedByUserID,
           ISNULL(ReviewDate, RequestDate),
           CASE WHEN RequestStatus = 'Approved'
                THEN LEFT(N'Approved before the step-by-step process. ' + ISNULL(Remarks, N''), 500)
                ELSE Remarks END
    FROM dbo.NewItemRequests
    WHERE RequestStatus IN ('Approved', 'Rejected', 'Fulfilled', 'Cancelled')
      AND ReviewedByUserID IS NOT NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NewItemRequests_Status')
    ALTER TABLE dbo.NewItemRequests DROP CONSTRAINT CK_NewItemRequests_Status;
GO

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_NewItemRequests_Status')
    ALTER TABLE dbo.NewItemRequests DROP CONSTRAINT DF_NewItemRequests_Status;
GO

UPDATE dbo.NewItemRequests SET RequestStatus = 'AwaitingDeptHead' WHERE RequestStatus = 'Pending';
UPDATE dbo.NewItemRequests SET RequestStatus = 'Procuring'        WHERE RequestStatus = 'Approved';
UPDATE dbo.NewItemRequests SET RequestStatus = 'Arrived'          WHERE RequestStatus = 'Fulfilled';
GO

ALTER TABLE dbo.NewItemRequests ADD CONSTRAINT DF_NewItemRequests_Status
    DEFAULT ('AwaitingDeptHead') FOR RequestStatus;
GO

ALTER TABLE dbo.NewItemRequests WITH CHECK ADD CONSTRAINT CK_NewItemRequests_Status CHECK (
    RequestStatus IN ('AwaitingDeptHead', 'AwaitingBudget', 'Procuring', 'Arrived', 'Rejected', 'Cancelled'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NewItemRequestSteps_Status')
    ALTER TABLE dbo.NewItemRequestSteps WITH CHECK ADD CONSTRAINT CK_NewItemRequestSteps_Status CHECK (
        Status IN ('AwaitingDeptHead', 'AwaitingBudget', 'Procuring', 'Arrived', 'Rejected', 'Cancelled'));
GO
