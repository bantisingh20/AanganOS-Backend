SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF NOT EXISTS (
    SELECT 1 FROM [NotificationTemplates]
    WHERE [Key] = N'admin.society.activated' AND [CommunityId] IS NULL
)
BEGIN
    INSERT INTO [NotificationTemplates]
        ([Id], [Key], [Scope], [CommunityId], [Category], [CategoryKind],
         [TitleTemplate], [BodyTemplate], [Channels], [IsActive], [IsDeleted],
         [CreatedAtUtc], [CreatedBy])
    VALUES
        ('626b9129-92f4-db5c-5578-d1aea07d788c',
         N'admin.society.activated',
         0,
         NULL,
         N'account',
         1,
         N'Your society "{societyName}" is now active',
         N'Hi {name}, good news — {societyName} has been approved and activated.' + CHAR(10) + CHAR(10) +
         N'Admin portal: {portalUrl}' + CHAR(10) +
         N'Sign in there with the same username and password you used when registering your society (username: {loginId}).' + CHAR(10) + CHAR(10) +
         N'To use the mobile app, register yourself as a resident from the app''s "Create account" screen.' + CHAR(10) + CHAR(10) +
         N'Welcome aboard.',
         5,
         1,
         0,
         '2026-01-01T00:00:00',
         N'seed');
END
