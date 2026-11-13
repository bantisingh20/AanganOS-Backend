namespace App.Domain.Identity;

/// <summary>
/// The granular action verbs a <see cref="Permission"/> confers and the Authorization_Service
/// evaluates against (Req 3.2). Each <see cref="Permission"/> is a single (ResourceType, Action)
/// pair; a Role grants a set of them.
///
/// <para>This enum lives in the Domain because it is a core part of the permission model (the
/// Action column of the Permission entity). It is the single definition reused across the stack:
/// the Task 2.2 <c>PermissionRequirement</c>/<c>RequirePermissionAttribute</c> seam and the Task
/// 5.3 evaluator both reference this type, so an endpoint's declared requirement and a stored
/// permission speak the same vocabulary.</para>
/// </summary>
public enum PermissionAction
{
    View,
    Create,
    Edit,
    Delete,
    Approve,
    Export,
    Assign,

    /// <summary>
    /// Mobile menu visibility. When a role holds (Resource, Access) the corresponding Home/Services
    /// tile is surfaced in the app even if the role lacks the feature's functional action. Menu
    /// items list Access alongside their functional action so EITHER grant reveals the tile; it is a
    /// pure visibility grant and confers no operation by itself (every endpoint still enforces its
    /// own functional permission).
    /// </summary>
    Access = 7,
}
