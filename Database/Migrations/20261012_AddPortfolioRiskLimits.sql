SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Portfolios', 'MaxRiskPerPosition') IS NULL
BEGIN
    ALTER TABLE dbo.Portfolios
        ADD MaxRiskPerPosition DECIMAL(19,4) NOT NULL
            CONSTRAINT DF_Portfolios_MaxRiskPerPosition DEFAULT (5.0000) WITH VALUES;
END;

IF COL_LENGTH('dbo.Portfolios', 'MaxExposurePerAsset') IS NULL
BEGIN
    ALTER TABLE dbo.Portfolios
        ADD MaxExposurePerAsset DECIMAL(19,4) NOT NULL
            CONSTRAINT DF_Portfolios_MaxExposurePerAsset DEFAULT (100.0000) WITH VALUES;
END;

IF COL_LENGTH('dbo.Portfolios', 'DailyLossLimit') IS NULL
BEGIN
    ALTER TABLE dbo.Portfolios
        ADD DailyLossLimit DECIMAL(19,4) NOT NULL
            CONSTRAINT DF_Portfolios_DailyLossLimit DEFAULT (10.0000) WITH VALUES;
END;

IF COL_LENGTH('dbo.Portfolios', 'MaxOpenPositions') IS NULL
BEGIN
    ALTER TABLE dbo.Portfolios
        ADD MaxOpenPositions INT NOT NULL
            CONSTRAINT DF_Portfolios_MaxOpenPositions DEFAULT (3) WITH VALUES;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.Portfolios')
      AND name = 'CK_Portfolios_RiskLimits'
)
BEGIN
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.Portfolios WITH CHECK
            ADD CONSTRAINT CK_Portfolios_RiskLimits
            CHECK (MaxRiskPerPosition >= 0 AND MaxExposurePerAsset >= 0
                   AND DailyLossLimit >= 0 AND MaxOpenPositions >= 0);';
END;

COMMIT TRANSACTION;