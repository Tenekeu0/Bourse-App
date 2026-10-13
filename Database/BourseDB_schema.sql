CREATE TABLE dbo.Users
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    UserName        NVARCHAR(100) NOT NULL,
    Email           NVARCHAR(254) NOT NULL,
    CreatedAt       DATETIME2(3) NOT NULL
        CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Users PRIMARY KEY (Id),
    CONSTRAINT UQ_Users_UserName UNIQUE (UserName),
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);
GO

CREATE TABLE dbo.Portfolios
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    UserId          BIGINT NOT NULL,
    Name            NVARCHAR(150) NOT NULL,
    Currency        CHAR(3) NOT NULL
        CONSTRAINT DF_Portfolios_Currency DEFAULT 'CAD',
    InitialCapital  DECIMAL(19,4) NOT NULL,
    MaxRiskPerPosition DECIMAL(19,4) NOT NULL,
    MaxExposurePerAsset DECIMAL(19,4) NOT NULL,
    DailyLossLimit  DECIMAL(19,4) NOT NULL,
    MaxOpenPositions INT NOT NULL,
    CreatedAt       DATETIME2(3) NOT NULL
        CONSTRAINT DF_Portfolios_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Portfolios PRIMARY KEY (Id),
    CONSTRAINT FK_Portfolios_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT CK_Portfolios_InitialCapital
        CHECK (InitialCapital >= 0),
    CONSTRAINT CK_Portfolios_RiskLimits
        CHECK (MaxRiskPerPosition >= 0 AND MaxExposurePerAsset >= 0 AND DailyLossLimit >= 0 AND MaxOpenPositions >= 0)
);
GO

CREATE INDEX IX_Portfolios_UserId
    ON dbo.Portfolios(UserId);
GO

CREATE TABLE dbo.MarketExchanges
(
    Id          INT IDENTITY(1,1) NOT NULL,
    Code        VARCHAR(10) NOT NULL,
    Name        NVARCHAR(150) NOT NULL,
    Country     CHAR(2) NOT NULL,

    CONSTRAINT PK_MarketExchanges PRIMARY KEY (Id),
    CONSTRAINT UQ_MarketExchanges_Code UNIQUE (Code)
);
GO

CREATE TABLE dbo.Instruments
(
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    Ticker      VARCHAR(20) NOT NULL,
    ExchangeId  INT NOT NULL,
    Name        NVARCHAR(200) NOT NULL,
    Currency    CHAR(3) NOT NULL
        CONSTRAINT DF_Instruments_Currency DEFAULT 'CAD',
    IsActive    BIT NOT NULL
        CONSTRAINT DF_Instruments_IsActive DEFAULT 1,
    CreatedAt   DATETIME2(3) NOT NULL
        CONSTRAINT DF_Instruments_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Instruments PRIMARY KEY (Id),
    CONSTRAINT FK_Instruments_MarketExchanges
        FOREIGN KEY (ExchangeId) REFERENCES dbo.MarketExchanges(Id),
    CONSTRAINT UQ_Instruments_Ticker_Exchange
        UNIQUE (Ticker, ExchangeId)
);
GO

CREATE INDEX IX_Instruments_IsActive
    ON dbo.Instruments(IsActive, Ticker);
GO

CREATE TABLE dbo.MarketDataSources
(
    Id          INT IDENTITY(1,1) NOT NULL,
    Name        NVARCHAR(100) NOT NULL,
    ApiKeyName  NVARCHAR(100) NULL,
    IsActive    BIT NOT NULL
        CONSTRAINT DF_MarketDataSources_IsActive DEFAULT 1,

    CONSTRAINT PK_MarketDataSources PRIMARY KEY (Id),
    CONSTRAINT UQ_MarketDataSources_Name UNIQUE (Name)
);
GO

CREATE TABLE dbo.MarketPrices
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    InstrumentId    BIGINT NOT NULL,
    SourceId        INT NOT NULL,
    Price           DECIMAL(19,6) NOT NULL,
    Volume          BIGINT NULL,
    TimestampUtc    DATETIME2(3) NOT NULL,
    DataQuality     VARCHAR(20) NOT NULL,
    IsDelayed       BIT NOT NULL
        CONSTRAINT DF_MarketPrices_IsDelayed DEFAULT 0,
    IsRealTime      BIT NOT NULL
        CONSTRAINT DF_MarketPrices_IsRealTime DEFAULT 0,

    CONSTRAINT PK_MarketPrices PRIMARY KEY (Id),
    CONSTRAINT FK_MarketPrices_Instruments
        FOREIGN KEY (InstrumentId) REFERENCES dbo.Instruments(Id),
    CONSTRAINT FK_MarketPrices_Sources
        FOREIGN KEY (SourceId) REFERENCES dbo.MarketDataSources(Id),
    CONSTRAINT CK_MarketPrices_Price CHECK (Price >= 0),
    CONSTRAINT CK_MarketPrices_Volume CHECK (Volume IS NULL OR Volume >= 0),
    CONSTRAINT CK_MarketPrices_DataQuality
        CHECK (DataQuality IN ('Valid', 'Suspect', 'Invalid', 'Missing')),
    CONSTRAINT CK_MarketPrices_TimeFlags
        CHECK (NOT (IsDelayed = 1 AND IsRealTime = 1))
);
GO

CREATE INDEX IX_MarketPrices_Instrument_Timestamp
    ON dbo.MarketPrices(InstrumentId, TimestampUtc DESC)
    INCLUDE (Price, Volume, SourceId, DataQuality, IsDelayed, IsRealTime);
GO

CREATE TABLE dbo.Strategies
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    PortfolioId     BIGINT NOT NULL,
    Name            NVARCHAR(150) NOT NULL,
    Description     NVARCHAR(2000) NULL,
    IsEnabled       BIT NOT NULL
        CONSTRAINT DF_Strategies_IsEnabled DEFAULT 0,
    CreatedAt       DATETIME2(3) NOT NULL
        CONSTRAINT DF_Strategies_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Strategies PRIMARY KEY (Id),
    CONSTRAINT FK_Strategies_Portfolios
        FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(Id)
);
GO

CREATE INDEX IX_Strategies_PortfolioId
    ON dbo.Strategies(PortfolioId, IsEnabled);
GO

CREATE TABLE dbo.StrategyParameters
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    StrategyId      BIGINT NOT NULL,
    ParameterName   NVARCHAR(100) NOT NULL,
    ParameterValue  NVARCHAR(1000) NOT NULL,
    CreatedAt       DATETIME2(3) NOT NULL
        CONSTRAINT DF_StrategyParameters_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_StrategyParameters PRIMARY KEY (Id),
    CONSTRAINT FK_StrategyParameters_Strategies
        FOREIGN KEY (StrategyId) REFERENCES dbo.Strategies(Id),
    CONSTRAINT UQ_StrategyParameters_Name
        UNIQUE (StrategyId, ParameterName)
);
GO

CREATE TABLE dbo.Signals
(
    Id                  BIGINT IDENTITY(1,1) NOT NULL,
    StrategyId          BIGINT NOT NULL,
    InstrumentId        BIGINT NOT NULL,
    SignalType          VARCHAR(10) NOT NULL,
    SignalStrength      DECIMAL(9,6) NOT NULL,
    CalculatedAtUtc     DATETIME2(3) NOT NULL,
    SourcePrice         DECIMAL(19,6) NOT NULL,
    Notes               NVARCHAR(1000) NULL,

    CONSTRAINT PK_Signals PRIMARY KEY (Id),
    CONSTRAINT FK_Signals_Strategies
        FOREIGN KEY (StrategyId) REFERENCES dbo.Strategies(Id),
    CONSTRAINT FK_Signals_Instruments
        FOREIGN KEY (InstrumentId) REFERENCES dbo.Instruments(Id),
    CONSTRAINT CK_Signals_Type
        CHECK (SignalType IN ('Buy', 'Sell', 'Hold')),
    CONSTRAINT CK_Signals_Strength
        CHECK (SignalStrength BETWEEN 0 AND 1),
    CONSTRAINT CK_Signals_SourcePrice
        CHECK (SourcePrice >= 0)
);
GO

CREATE INDEX IX_Signals_Strategy_CalculatedAt
    ON dbo.Signals(StrategyId, CalculatedAtUtc DESC);
GO

CREATE TABLE dbo.Positions
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    PortfolioId     BIGINT NOT NULL,
    InstrumentId    BIGINT NOT NULL,
    Quantity        DECIMAL(19,6) NOT NULL,
    AveragePrice    DECIMAL(19,6) NOT NULL,
    CurrentPrice    DECIMAL(19,6) NULL,
    UpdatedAtUtc    DATETIME2(3) NOT NULL
        CONSTRAINT DF_Positions_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Positions PRIMARY KEY (Id),
    CONSTRAINT FK_Positions_Portfolios
        FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(Id),
    CONSTRAINT FK_Positions_Instruments
        FOREIGN KEY (InstrumentId) REFERENCES dbo.Instruments(Id),
    CONSTRAINT UQ_Positions_Portfolio_Instrument
        UNIQUE (PortfolioId, InstrumentId),
    CONSTRAINT CK_Positions_Quantity CHECK (Quantity >= 0),
    CONSTRAINT CK_Positions_AveragePrice CHECK (AveragePrice >= 0),
    CONSTRAINT CK_Positions_CurrentPrice
        CHECK (CurrentPrice IS NULL OR CurrentPrice >= 0)
);
GO

CREATE TABLE dbo.Orders
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    PortfolioId     BIGINT NOT NULL,
    InstrumentId    BIGINT NOT NULL,
    UserId          BIGINT NOT NULL,
    OrderSide       VARCHAR(10) NOT NULL,
    Quantity        DECIMAL(19,6) NOT NULL,
    EstimatedPrice  DECIMAL(19,6) NOT NULL,
    Status          VARCHAR(20) NOT NULL
        CONSTRAINT DF_Orders_Status DEFAULT 'Proposed',
    ReasonRejected  NVARCHAR(1000) NULL,
    CreatedAtUtc    DATETIME2(3) NOT NULL
        CONSTRAINT DF_Orders_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    ConfirmedAtUtc  DATETIME2(3) NULL,

    CONSTRAINT PK_Orders PRIMARY KEY (Id),
    CONSTRAINT FK_Orders_Portfolios
        FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(Id),
    CONSTRAINT FK_Orders_Instruments
        FOREIGN KEY (InstrumentId) REFERENCES dbo.Instruments(Id),
    CONSTRAINT FK_Orders_Users
        FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT CK_Orders_Side
        CHECK (OrderSide IN ('Buy', 'Sell')),
    CONSTRAINT CK_Orders_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_Orders_EstimatedPrice CHECK (EstimatedPrice > 0),
    CONSTRAINT CK_Orders_Status
        CHECK (Status IN (
            'Proposed', 'Approved', 'Rejected',
            'Executed', 'Cancelled', 'Expired'
        )),
    CONSTRAINT CK_Orders_ConfirmedAt
        CHECK (Status NOT IN ('Approved', 'Executed')
               OR ConfirmedAtUtc IS NOT NULL)
);
GO

CREATE INDEX IX_Orders_Portfolio_CreatedAt
    ON dbo.Orders(PortfolioId, CreatedAtUtc DESC);
GO

CREATE INDEX IX_Orders_Status_CreatedAt
    ON dbo.Orders(Status, CreatedAtUtc);
GO

CREATE TABLE dbo.OrderEvents
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    OrderId         BIGINT NOT NULL,
    PreviousStatus  VARCHAR(20) NULL,
    NewStatus       VARCHAR(20) NOT NULL,
    EventAtUtc      DATETIME2(3) NOT NULL
        CONSTRAINT DF_OrderEvents_EventAtUtc DEFAULT SYSUTCDATETIME(),
    Message         NVARCHAR(1000) NULL,

    CONSTRAINT PK_OrderEvents PRIMARY KEY (Id),
    CONSTRAINT FK_OrderEvents_Orders
        FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id),
    CONSTRAINT CK_OrderEvents_NewStatus
        CHECK (NewStatus IN (
            'Proposed', 'Approved', 'Rejected',
            'Executed', 'Cancelled', 'Expired'
        ))
);
GO

CREATE INDEX IX_OrderEvents_OrderId_EventAtUtc
    ON dbo.OrderEvents(OrderId, EventAtUtc);
GO

CREATE TABLE dbo.BacktestRuns
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    PortfolioId     BIGINT NOT NULL,
    StrategyId      BIGINT NOT NULL,
    StartDate       DATE NOT NULL,
    EndDate         DATE NOT NULL,
    Status          VARCHAR(20) NOT NULL
        CONSTRAINT DF_BacktestRuns_Status DEFAULT 'Pending',
    CreatedAtUtc    DATETIME2(3) NOT NULL
        CONSTRAINT DF_BacktestRuns_CreatedAtUtc DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_BacktestRuns PRIMARY KEY (Id),
    CONSTRAINT FK_BacktestRuns_Portfolios
        FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(Id),
    CONSTRAINT FK_BacktestRuns_Strategies
        FOREIGN KEY (StrategyId) REFERENCES dbo.Strategies(Id),
    CONSTRAINT CK_BacktestRuns_Dates CHECK (EndDate >= StartDate),
    CONSTRAINT CK_BacktestRuns_Status
        CHECK (Status IN ('Pending', 'Running', 'Completed', 'Failed', 'Cancelled'))
);
GO

CREATE INDEX IX_BacktestRuns_Portfolio_CreatedAt
    ON dbo.BacktestRuns(PortfolioId, CreatedAtUtc DESC);
GO

CREATE TABLE dbo.BacktestMetrics
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    BacktestRunId   BIGINT NOT NULL,
    NetReturn       DECIMAL(19,8) NOT NULL,
    MaxDrawdown     DECIMAL(19,8) NOT NULL,
    NumberOfTrades  INT NOT NULL,
    WinRate         DECIMAL(9,6) NOT NULL,
    TotalCosts      DECIMAL(19,6) NOT NULL,
    CreatedAtUtc    DATETIME2(3) NOT NULL
        CONSTRAINT DF_BacktestMetrics_CreatedAtUtc DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_BacktestMetrics PRIMARY KEY (Id),
    CONSTRAINT FK_BacktestMetrics_BacktestRuns
        FOREIGN KEY (BacktestRunId) REFERENCES dbo.BacktestRuns(Id),
    CONSTRAINT UQ_BacktestMetrics_BacktestRunId UNIQUE (BacktestRunId),
    CONSTRAINT CK_BacktestMetrics_Drawdown
        CHECK (MaxDrawdown BETWEEN 0 AND 1),
    CONSTRAINT CK_BacktestMetrics_Trades
        CHECK (NumberOfTrades >= 0),
    CONSTRAINT CK_BacktestMetrics_WinRate
        CHECK (WinRate BETWEEN 0 AND 1),
    CONSTRAINT CK_BacktestMetrics_Costs
        CHECK (TotalCosts >= 0)
);
GO

CREATE TABLE dbo.AuditLogs
(
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    EntityType      NVARCHAR(100) NOT NULL,
    EntityId        BIGINT NOT NULL,
    Action          NVARCHAR(100) NOT NULL,
    Message         NVARCHAR(2000) NULL,
    CreatedAtUtc    DATETIME2(3) NOT NULL
        CONSTRAINT DF_AuditLogs_CreatedAtUtc DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_AuditLogs PRIMARY KEY (Id)
);
GO

CREATE INDEX IX_AuditLogs_Entity_CreatedAt
    ON dbo.AuditLogs(EntityType, EntityId, CreatedAtUtc DESC);
GO
