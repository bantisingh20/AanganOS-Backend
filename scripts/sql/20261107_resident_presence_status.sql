SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF COL_LENGTH('Residents', 'PresenceStatus') IS NULL
BEGIN
    ALTER TABLE Residents ADD PresenceStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_Resident_PresenceStatus DEFAULT('at_home');
END

SELECT COL_LENGTH('Residents', 'PresenceStatus') AS PresenceStatusCol;
