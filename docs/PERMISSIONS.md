# API permissions (server-side RBAC)

Last updated: 27 Nov 2026.

## How it works

- Every protected endpoint is marked `[RequirePermission(Action, "Resource")]`. There are 250 of them across `src/App.Api/Controllers`.
- `PermissionAuthorizationMiddleware` reads the attribute. It asks `IPermissionEvaluator.IsPermittedAsync` (`AuthorizationService`) whether the caller's role holds that grant (`Users.RoleId → RolePermissions → Permissions`). If not, it throws `ForbiddenException`, which returns **403 FORBIDDEN**.
- **Super admin bypass:** `CurrentUser.IsSuperAdmin` is set at login (`AuthService`) when the role is named `Superadmin`, **or** when the user has no `CommunityId` and is not an OrganizationAdmin. Those users pass every check.
- **No admin bypass.** OrganizationAdmin and CommunityAdmin get only what their role holds in `RolePermissions`. This differs from the frontend rule `isAdminRole` (`packages/shared/src/lib/roles.ts`), which treats any role containing "admin", plus PowerUser and SuperAdmin, as having full rights on every page they can see. Migration `00000008` closes the gap with grants, without adding a bypass (see below).
- **Matching is fuzzy.** `ResourceMatches` strips the `app.` / `admin.` / `features.` prefixes, folds singular and plural, matches any dot/dash segment of 4+ characters that is a substring, and applies alias rules (helpdesk→ticket, billing→bill/charge/payment, resident→user, household→pet/vehicle, …). An old menu grant such as `app.billing.bills` can therefore satisfy `Create:MaintenanceBill`. Matrix cells marked ✅≈ pass only through this fuzzy match. See "Follow-ups".
- Menu visibility (`/api/v1/menu`, `MenuService`) uses **exact** `RequiredPermissionResource:Action` pairs, so the new entity-level rows do not change which menus anyone sees.

## The switch: `Security:EnforcePermissions`

| Value | Behaviour |
|---|---|
| `true` | 403 when the grant is missing. Each denial is logged at Information: `[RBAC] Denied …`. |
| `false` | **Audit only.** The check still runs and the request is let through. Each would-be denial is logged at Warning: `[RBAC audit] would deny {Action}:{Resource} for user … (roles …) on {Method} {Path}`. |
| not set | `true` everywhere except the Development environment. |

- `appsettings.json` sets `true`.
- `appsettings.Development.json` sets `false`, because local dev points at the shared database.
- To override, set the environment variable `Security__EnforcePermissions=true|false`.
- The effective value is logged once at startup.

**Suggested rollout:**

1. Deploy with `Security__EnforcePermissions=false`. Migration `00000008` runs at startup.
2. Use the apps as each role for a day.
3. Search the logs for `[RBAC audit] would deny`. Every hit is a request that would get 403.
4. Fix any gaps with a grant.
5. Remove the override (it defaults to `true`).

## Before / after summary (250 protected endpoints)

| Role | Before (today, check commented out) | Enforced with seed grants only (00000000–00000007) | Enforced + `00000008` |
|---|---|---|---|
| Resident | 250 (any signed-in user) | 52 | 102 (26 of them via fuzzy match ≈) |
| Security | 250 | 13 | 69 (15 ≈) |
| CommunityAdmin | 250 | 12 | 229 (2 ≈) |
| OrganizationAdmin | 250 | 12 | 231 (1 ≈) |
| SuperAdmin | 250 | 250 (bypass) | 250 (bypass) |

**Why the migration is needed.**

- The catalog was built from menu keys (`visitors`, `app.walkin`, `admin.roles`, …), but endpoints use entity names (`Visitor`, `EntryExitEvent`, `Role`, …). 117 of the 250 endpoints had **no** permission row that matches, even fuzzily.
- The seeds give CommunityAdmin and OrganizationAdmin only `residents` and `app.admin.resident-approvals` (`00000001`).
- With seed grants only, enforcement would have broken the core flows:
  - residents could not raise a ticket, invite a visitor, approve a walk-in, or add household members;
  - guards could not verify a pass, register a walk-in, record exit, or acknowledge an SOS;
  - admins could not use the admin site at all.

**Caveat: these numbers come from the seed scripts, not from the live database.** Grants added in the admin Roles page are not in the scripts; `00000006` notes that admins also hold `admin.users`, for example. Live roles can only have **more** than shown here. To see the real grants without running the API, run this read-only query:

```sql
SELECT r.Name AS RoleName, r.CommunityId, p.ResourceType, p.Action
FROM dbo.Roles r
JOIN dbo.RolePermissions rp ON rp.RoleId = r.Id
JOIN dbo.Permissions p ON p.Id = rp.PermissionId AND p.IsDeleted = 0
WHERE r.IsDeleted = 0
ORDER BY r.Name, p.ResourceType, p.Action;
```

Also check for custom roles, i.e. any `dbo.Roles` row other than the five system roles (for example `SocietyAdmin`, which `AnnouncementService` refers to). `00000008` grants nothing to those roles. Under enforcement they rely on their menu-key grants plus fuzzy matching. Audit mode will show what they lose.

## Migration `scripts/sql/00000008_endpoint_permission_grants.sql`

The script only **adds** rows and is idempotent. It was dry-run twice on a scratch LocalDB: the first run inserted 151 permission rows and 496 grants, the second inserted 0, and no duplicates were created.

1. **Catalog.** One `Permissions` row for each of the 152 `(Resource, Action)` pairs that an endpoint declares, using the exact entity name, so a seeded role never depends on fuzzy matching.
2. **SuperAdmin:** all 152 (it bypasses anyway; this keeps the Roles page truthful).
3. **OrganizationAdmin:** all except the platform-only list.
4. **CommunityAdmin:** the same as OrganizationAdmin, minus `Edit:Organization` and `Create:Community`.
5. **Platform-only (SuperAdmin):** `Organization` Create/Delete, `Community` Delete, `Role` Create/Edit/Delete, `UserRole` Assign, `Configuration` Edit (this also covers notification templates), `MasterData` Edit, `error_logs` View/Delete, `admin.communication-test` View/Edit.
   - The services behind these have no organization or community scope guard.
   - An admin holding them could edit the global system roles (and grant itself anything), platform settings, or other organizations.
6. **Resident:** what the resident screens call, plus resident self-service:
   - **View:** Announcement, Community, Organization, Unit, Resident, HouseholdMember, EmergencyContact, Pet, Vehicle, Ticket, TicketComment, TicketAttachment, MaintenanceBill, MaintenanceCharge, MaintenancePayment, Visitor, VisitPass, RecurringVisitor, Parcel, Amenity, FacilitySlot, BlackoutDate, Booking, EvReservation, ParkingAssignment.
   - **Create:** Ticket, TicketComment, VisitPass, RecurringVisitor, Resident, HouseholdMember, EmergencyContact, Pet, Vehicle, Booking, EvReservation, SosAlert, ParcelAuthorization.
   - **Edit:** Resident (My Profile), Booking, EvReservation.
   - **Approve:** Visitor (walk-in approval for own unit).
7. **Security:** what the guard screens call:
   - **View:** Announcement, Community, Organization, Unit, Resident, Visitor, VisitPass, RecurringVisitor, EntryExitEvent, Vehicle, WatchlistEntry, SosAlert, Parcel, GuardShift, Incident, ParkingViolation.
   - **Create:** Visitor, EntryExitEvent, WatchlistEntry, Incident, Parcel, SosAlert, ParkingViolation.
   - **Edit:** SosAlert, Parcel, Incident.

Grants go to the global system roles only (`CommunityId IS NULL`). The Roles page saves a role's full current grant set, so these grants survive later edits there.

**Rollback:**

- Delete the rows with `CreatedBy = 'migration-00000008'`; the exact SQL is in the script header.
- Or set `Security__EnforcePermissions=false` to drop back to audit mode instantly.

## Calls the apps still make that get 403 after `00000008`

These come from a static trace of `packages/shared/src/api/*` methods through the screens each app route imports.

| Role | Endpoint | Screen | Expected? |
|---|---|---|---|
| Resident | `POST /announcements`, `PUT/DELETE /announcements/{id}`, `POST /announcements/{id}/activate` and `/deactivate` | `/announcements` | Yes. Admin actions, hidden by `usePermission`. |
| Resident | `POST /tickets/{id}/status` | `/helpdesk` | Yes. Hidden unless `access.canChangeStatus`. |
| Resident | `POST /residents/{id}/verify` and `/reject` | `/my-tasks` | Yes. Residents only get their own "complete your KYC" card. |
| Security | announcement admin actions, `POST /tickets/{id}/status` | `/announcements`, `/home` | Yes, same reasons as above. |
| Security | `GET /tickets`, `GET /maintenance-bills/me`, `GET /maintenance-bills/{id}` | `/home` dashboard | Harmless. Gated by `canSeeHelpdesk` / bills, and errors are swallowed (`.catch(() => null)`). |
| Security | `PUT /residents/{id}` | `/my-profile` (edit) | **Regression.** Not granted on purpose: `ResidentService.UpdateAsync` has no ownership check, so this grant would let guards edit any resident. Needs an own-profile endpoint (see Follow-ups). |
| CommunityAdmin, OrganizationAdmin | error logs, Email & SMS test, Roles create/edit/delete, Config upsert/delete, notification-template upsert, master-data edit, Organization create/delete | admin site | Intended (platform-only). These pages and buttons need the menu to hide them for non-SuperAdmins, or users get 403 toasts. |
| CommunityAdmin | `PUT /organizations/{id}`, `POST /organizations/{id}/activate` | admin site, Organizations | Intended. |

## Endpoints with no `[RequirePermission]` (authentication only, unchanged)

AdminUsers (4), Auth (9), Config (4 reads), Files (3), Gate (1), MyTasks (2), Menu (5), Notifications (6), Onboarding (1), ResidentOnboarding (10), Subscriptions (2), UnitHistory (1), AppVersion (1).

**⚠ `POST /api/v1/admin-users` (AdminUsersController) has no permission or role check.** Any signed-in user, including a resident, can create a Superadmin account. The same applies to update and delete. This is the most serious hole found, and turning RBAC on does **not** close it.

## Follow-ups (not done here)

1. **Lock down AdminUsersController.** Add `[RequirePermission]` to all four actions, or a SuperAdmin/OrgAdmin check in `AdminUserService`, and stop non-SuperAdmins from creating Superadmin or PowerUser accounts.
2. **Tighten `ResourceMatches`.** Two examples:
   - The segment-substring rule makes `Unit` match `Community`, so a CommunityAdmin can `Create`/`Delete` communities.
   - The `resident→user` alias gives residents `Create/Edit:User`, and their seeded `app.billing.*` (0,1,2) grants give them `Create/Edit:MaintenanceBill` and `MaintenanceCharge`.

   Now that every endpoint has an exact-name row, exact matching (plus prefix and plural folding) would be enough for the system roles. Check custom roles in audit mode first.
3. **Add ownership checks.** `ResidentService.UpdateAsync` (and Create/Delete for household members) do not check that the record belongs to the caller. Add a `PUT /residents/me` for My Profile, then grant Security only that.
4. **Clean up admin menus.** Hide the platform-only menus (Error Logs, Email & SMS Testing, Roles edit, Config edit, Organizations create) from non-SuperAdmins in `MenuItems` / role grants, so admins don't see buttons that return 403.

## Per-endpoint matrix (after `00000008`, enforcement on)

Before = ✅ for every row and every signed-in user. ✅ = exact grant. ✅≈ = passes only through fuzzy matching of another grant. ❌ = 403.

| Endpoint | Requires | Resident | Security | CommunityAdmin | OrganizationAdmin | SuperAdmin |
|---|---|:-:|:-:|:-:|:-:|:-:|
| **Amenities** | | | | | | |
| `POST /api/v1/amenities` | Create:Amenity | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/amenities` | View:Amenity | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/amenities/{id:guid}` | View:Amenity | ✅ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/amenities/{id:guid}` | Edit:Amenity | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/amenities/{id:guid}` | Delete:Amenity | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/amenities/{id:guid}/slots` | Create:FacilitySlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/amenities/{id:guid}/slots` | View:FacilitySlot | ✅ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/amenities/{id:guid}/booking-rule` | Edit:Amenity | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/amenities/{id:guid}/booking-rule` | View:Amenity | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/amenities/{id:guid}/blackout-dates` | Create:BlackoutDate | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/amenities/{id:guid}/blackout-dates` | View:BlackoutDate | ✅ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/facility-slots/{slotId:guid}` | Delete:FacilitySlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/blackout-dates/{blackoutId:guid}` | Delete:BlackoutDate | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/bookings` | Create:Booking | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/bookings/{id:guid}` | View:Booking | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/bookings/{id:guid}/approve` | Approve:Booking | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/bookings/{id:guid}/cancel` | Edit:Booking | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/resident-events` | Create:ResidentEvent | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| `GET /api/v1/resident-events` | View:ResidentEvent | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| `GET /api/v1/resident-events/{id:guid}` | View:ResidentEvent | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/resident-events/{id:guid}/register` | Create:ResidentEvent | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/resident-event-registrations/{registrationId:guid}` | Create:ResidentEvent | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| **Announcements** | | | | | | |
| `GET /api/v1/announcements` | View:Announcement | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/announcements/{id:guid}` | View:Announcement | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/announcements` | Create:Announcement | ❌ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/announcements/{id:guid}` | Edit:Announcement | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/announcements/{id:guid}/activate` | Edit:Announcement | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/announcements/{id:guid}/deactivate` | Edit:Announcement | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/announcements/{id:guid}` | Delete:Announcement | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Audit** | | | | | | |
| `GET /api/v1/audit-logs` | View:Audit | ❌ | ❌ | ✅ | ✅ | ✅ |
| **CommunicationTest** | | | | | | |
| `GET /api/v1/communication-test/config-summary` | View:admin.communication-test | ❌ | ❌ | ❌ | ❌ | ✅ |
| `POST /api/v1/communication-test/email` | Edit:admin.communication-test | ❌ | ❌ | ❌ | ❌ | ✅ |
| `POST /api/v1/communication-test/sms` | Edit:admin.communication-test | ❌ | ❌ | ❌ | ❌ | ✅ |
| **Communities** | | | | | | |
| `POST /api/v1/communities` | Create:Community | ❌ | ❌ | ✅≈ | ✅ | ✅ |
| `GET /api/v1/communities` | View:Community | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/communities/{id:guid}` | View:Community | ✅ | ✅ | ✅ | ✅ | ✅ |
| `PUT /api/v1/communities/{id:guid}` | Edit:Community | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/communities/{id:guid}` | Delete:Community | ❌ | ❌ | ✅≈ | ✅≈ | ✅ |
| **Config** | | | | | | |
| `POST /api/v1/config` | Edit:Configuration | ❌ | ❌ | ❌ | ❌ | ✅ |
| `DELETE /api/v1/config/{id:guid}` | Edit:Configuration | ❌ | ❌ | ❌ | ❌ | ✅ |
| `PUT /api/v1/config/{key}` | Edit:Configuration | ❌ | ❌ | ❌ | ❌ | ✅ |
| `PUT /api/v1/master-data/{listKey}` | Edit:MasterData | ❌ | ❌ | ❌ | ❌ | ✅ |
| **ErrorLogs** | | | | | | |
| `GET /api/v1/error-logs` | View:error_logs | ❌ | ❌ | ❌ | ❌ | ✅ |
| `GET /api/v1/error-logs/{id:guid}` | View:error_logs | ❌ | ❌ | ❌ | ❌ | ✅ |
| `DELETE /api/v1/error-logs/{id:guid}` | Delete:error_logs | ❌ | ❌ | ❌ | ❌ | ✅ |
| `DELETE /api/v1/error-logs/clear` | Delete:error_logs | ❌ | ❌ | ❌ | ❌ | ✅ |
| **Gate** | | | | | | |
| `POST /api/v1/visit-passes` | Create:VisitPass | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/visit-passes/verify` | Create:Visitor | ✅≈ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/visit-passes/scan` | Create:EntryExitEvent | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/gate/vehicle-lookup` | View:Vehicle | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/visit-passes` | View:VisitPass | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/visit-passes/{id:guid}` | View:VisitPass | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/visit-passes/{id:guid}/share` | Create:VisitPass | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/visitors` | Create:Visitor | ✅≈ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/visitors/{id:guid}/approve` | Approve:Visitor | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/visitors/{id:guid}/reject` | Approve:Visitor | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/visitors` | View:Visitor | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/visitors/{id:guid}` | View:Visitor | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/entry-events/entry` | Create:EntryExitEvent | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/entry-events/exit` | Create:EntryExitEvent | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/entry-events` | View:EntryExitEvent | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/recurring-visitors` | Create:RecurringVisitor | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/recurring-visitors/{id:guid}/revoke` | Create:RecurringVisitor | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/recurring-visitors/{id:guid}/extend` | Create:RecurringVisitor | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `GET /api/v1/recurring-visitors` | View:RecurringVisitor | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/recurring-visitors/{id:guid}` | View:RecurringVisitor | ✅ | ✅ | ✅ | ✅ | ✅ |
| **HierarchyNodes** | | | | | | |
| `POST /api/v1/hierarchy-nodes` | Create:HierarchyNode | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/communities/{communityId:guid}/hierarchy` | View:HierarchyNode | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/hierarchy-nodes/{id:guid}` | View:HierarchyNode | ❌ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/hierarchy-nodes/{id:guid}` | Edit:HierarchyNode | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/hierarchy-nodes/{id:guid}` | Delete:HierarchyNode | ❌ | ❌ | ✅ | ✅ | ✅ |
| **MaintenanceBilling** | | | | | | |
| `POST /api/v1/maintenance-charges` | Create:MaintenanceCharge | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-charges/bulk` | Create:MaintenanceCharge | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-charges` | View:MaintenanceCharge | ✅ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/maintenance-charges/{id:guid}` | Edit:MaintenanceCharge | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-bills/generate` | Create:MaintenanceBill | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills/me` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills/defaulters` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills/collection-summary` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-expenses` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-expenses` | Create:MaintenanceBill | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/maintenance-expenses/{id:guid}` | Edit:MaintenanceBill | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/maintenance-expenses/{id:guid}` | Edit:MaintenanceBill | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills/me/due` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills/{id:guid}` | View:MaintenanceBill | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-bills/{id:guid}/payments` | View:MaintenancePayment | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-bills/{id:guid}/waive` | Edit:MaintenanceBill | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-payments` | Create:MaintenancePayment | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-payments/submit` | Create:MaintenancePayment | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-payments/pending` | Approve:MaintenancePayment | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-payments/{id:guid}/approve` | Approve:MaintenancePayment | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-payments/{id:guid}/reject` | Approve:MaintenancePayment | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Maintenance** | | | | | | |
| `POST /api/v1/assets` | Create:Asset | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/assets` | View:Asset | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/assets/{id:guid}` | View:Asset | ❌ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/assets/{id:guid}` | Edit:Asset | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/assets/{id:guid}` | Delete:Asset | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/assets/{id:guid}/equipment-history` | View:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/assets/{id:guid}/inspections` | View:InspectionChecklist | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/assets/{id:guid}/amc-contracts` | View:AmcContract | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/assets/{id:guid}/meter-readings` | View:MeterReading | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/maintenance-plans` | Create:MaintenancePlan | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-plans` | View:MaintenancePlan | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/maintenance-plans/{id:guid}` | View:MaintenancePlan | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/maintenance-plans/{id:guid}` | Edit:MaintenancePlan | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/work-orders` | Create:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/work-orders` | View:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/work-orders/{id:guid}` | View:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/work-orders/{id:guid}/assign` | Assign:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/work-orders/{id:guid}/status` | Edit:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/work-orders/{id:guid}/parts` | Edit:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/work-orders/{id:guid}/parts` | View:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/work-orders/{id:guid}/labour` | Edit:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/work-orders/{id:guid}/labour` | View:WorkOrder | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/inspections` | Create:InspectionChecklist | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/inspections/{id:guid}` | View:InspectionChecklist | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/amc-contracts` | Create:AmcContract | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/amc-contracts/{id:guid}` | View:AmcContract | ❌ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/amc-contracts/{id:guid}` | Edit:AmcContract | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/meter-readings` | Create:MeterReading | ❌ | ❌ | ✅ | ✅ | ✅ |
| **MessageOutbox** | | | | | | |
| `GET /api/v1/message-outbox` | View:Audit | ❌ | ❌ | ✅ | ✅ | ✅ |
| **NotificationOutbox** | | | | | | |
| `GET /api/v1/notification-outbox` | View:Audit | ❌ | ❌ | ✅ | ✅ | ✅ |
| **NotificationTemplates** | | | | | | |
| `GET /api/v1/notification-templates` | View:Configuration | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/notification-templates/{id:guid}` | View:Configuration | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/notification-templates` | Edit:Configuration | ❌ | ❌ | ❌ | ❌ | ✅ |
| `POST /api/v1/notification-templates/preview` | View:Configuration | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Organizations** | | | | | | |
| `POST /api/v1/organizations` | Create:Organization | ❌ | ❌ | ❌ | ❌ | ✅ |
| `GET /api/v1/organizations` | View:Organization | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/organizations/{id:guid}` | View:Organization | ✅ | ✅ | ✅ | ✅ | ✅ |
| `PUT /api/v1/organizations/{id:guid}` | Edit:Organization | ❌ | ❌ | ❌ | ✅ | ✅ |
| `POST /api/v1/organizations/{id:guid}/activate` | Edit:Organization | ❌ | ❌ | ❌ | ✅ | ✅ |
| `DELETE /api/v1/organizations/{id:guid}` | Delete:Organization | ❌ | ❌ | ❌ | ❌ | ✅ |
| **Parcels** | | | | | | |
| `POST /api/v1/parcels` | Create:Parcel | ✅≈ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/parcels` | View:Parcel | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/parcels/{id:guid}` | View:Parcel | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/parcels/{id:guid}/status` | Edit:Parcel | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/parcels/{id:guid}/custody-location` | Edit:Parcel | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/parcels/{id:guid}/authorizations` | Create:ParcelAuthorization | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/parcels/{id:guid}/handover` | Edit:Parcel | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/parcels/{id:guid}/lost-damaged` | Edit:Parcel | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/parcels/{id:guid}/returned` | Edit:Parcel | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/parcels/{id:guid}/custody-timeline` | View:CustodyTimelineEntry | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/custody-locations` | Create:CustodyLocation | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Parking** | | | | | | |
| `POST /api/v1/parking-zones` | Create:ParkingZone | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-zones` | View:ParkingZone | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/parking-slots` | Create:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-slots` | View:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-slots/occupancy` | View:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-slots/{id:guid}` | View:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/parking-slots/{id:guid}` | Edit:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-slots/{id:guid}/assignments` | View:ParkingAssignment | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-slots/{id:guid}/violations` | View:ParkingViolation | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/parking-slots/{id:guid}/ev-reservations` | View:EvReservation | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/parking-assignments` | Assign:ParkingAssignment | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/parking-assignments/release` | Assign:ParkingAssignment | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/parking-violations` | Create:ParkingViolation | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/parking-violations/{id:guid}/status` | Edit:ParkingViolation | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/guest-parking` | Assign:ParkingAssignment | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/ev-reservations` | Create:EvReservation | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/ev-reservations/{id:guid}/close` | Edit:EvReservation | ✅ | ❌ | ✅ | ✅ | ✅ |
| **Permissions** | | | | | | |
| `GET /api/v1/permissions` | View:Permission | ❌ | ❌ | ✅ | ✅ | ✅ |
| **ResidentAssets** | | | | | | |
| `POST /api/v1/vehicles` | Create:Vehicle | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/pets` | Create:Pet | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/resident-parking-slots` | Create:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/resident-parking-slots/{id:guid}/assignment` | Assign:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/storage-locations` | Create:StorageLocation | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/storage-locations/{id:guid}/assignment` | Assign:StorageLocation | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{unitId:guid}/vehicles` | View:Vehicle | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{unitId:guid}/pets` | View:Pet | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{unitId:guid}/parking-slots` | View:ParkingSlot | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{unitId:guid}/storage-locations` | View:StorageLocation | ❌ | ❌ | ✅ | ✅ | ✅ |
| **ResidentOnboarding** | | | | | | |
| `GET /api/v1/residents/join-requests` | View:JoinRequest | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/join-requests/{id:guid}/approve` | Approve:JoinRequest | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/join-requests/{id:guid}/reject` | Approve:JoinRequest | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Residents** | | | | | | |
| `POST /api/v1/residents` | Create:Resident | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/residents` | View:Resident | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/residents/me` | View:Resident | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/me/presence` | View:Resident | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/me/submit-kyc` | View:Resident | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/residents/{id:guid}` | View:Resident | ✅ | ✅ | ✅ | ✅ | ✅ |
| `PUT /api/v1/residents/{id:guid}` | Edit:Resident | ✅ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/residents/{id:guid}` | Delete:Resident | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/verify` | Approve:Resident | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/reject` | Approve:Resident | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/resubmit` | Approve:Resident | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/move-in` | Create:Resident | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/move-out` | Edit:Resident | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/household` | Create:HouseholdMember | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/residents/{id:guid}/household` | View:HouseholdMember | ✅ | ✅≈ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/login` | Create:User | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/residents/{id:guid}/emergency-contacts` | Create:EmergencyContact | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/residents/{id:guid}/emergency-contacts` | View:EmergencyContact | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/residents/{id:guid}/communication-preferences` | View:Resident | ✅ | ✅ | ✅ | ✅ | ✅ |
| `PUT /api/v1/residents/{id:guid}/communication-preferences` | Edit:Resident | ✅ | ❌ | ✅ | ✅ | ✅ |
| **Roles** | | | | | | |
| `POST /api/v1/roles` | Create:Role | ❌ | ❌ | ❌ | ❌ | ✅ |
| `GET /api/v1/roles` | View:Role | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/roles/{id:guid}` | View:Role | ❌ | ❌ | ✅ | ✅ | ✅ |
| `PUT /api/v1/roles/{id:guid}` | Edit:Role | ❌ | ❌ | ❌ | ❌ | ✅ |
| `DELETE /api/v1/roles/{id:guid}` | Delete:Role | ❌ | ❌ | ❌ | ❌ | ✅ |
| **SecurityOps** | | | | | | |
| `POST /api/v1/watchlist` | Create:WatchlistEntry | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/watchlist/{id:guid}/clear` | Create:WatchlistEntry | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/watchlist` | View:WatchlistEntry | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/guard-shifts` | Create:GuardShift | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/guard-shifts` | View:GuardShift | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/incidents` | Create:Incident | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/incidents/{id:guid}/acknowledge` | Edit:Incident | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/incidents/{id:guid}/resolve` | Edit:Incident | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/incidents` | View:Incident | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/sos-alerts` | Create:SosAlert | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/sos-alerts/{id:guid}/acknowledge` | Edit:SosAlert | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/sos-alerts/{id:guid}/resolve` | Edit:SosAlert | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/sos-alerts` | View:SosAlert | ❌ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/access-integration` | Create:Incident | ❌ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/access-integration` | View:Incident | ❌ | ✅ | ✅ | ✅ | ✅ |
| **Subscriptions** | | | | | | |
| `GET /api/v1/subscriptions/by-org/{orgId:guid}` | View:Community | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/subscriptions/set-expiry` | Edit:Community | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/subscriptions/renewals/pending` | View:Community | ✅ | ✅ | ✅ | ✅ | ✅ |
| `POST /api/v1/subscriptions/renewals/{id:guid}/approve` | Edit:Community | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Tickets** | | | | | | |
| `POST /api/v1/tickets` | Create:Ticket | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/tickets` | View:Ticket | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/tickets/analytics` | View:Ticket | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/tickets/{id:guid}` | View:Ticket | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/tickets/{id:guid}/attachments` | View:TicketAttachment | ✅ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/tickets/{id:guid}/comments` | View:TicketComment | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/tickets/{id:guid}/comments` | Create:TicketComment | ✅ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/tickets/{id:guid}/assign` | Assign:Ticket | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/tickets/{id:guid}/status` | Edit:Ticket | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/tickets/{id:guid}/feedback` | Create:Ticket | ✅ | ❌ | ✅ | ✅ | ✅ |
| **UnitHistory** | | | | | | |
| `GET /api/v1/units/{unitId:guid}/history` | View:OwnershipHistory | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{unitId:guid}/ownership-history` | View:OwnershipHistory | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{unitId:guid}/tenancy-history` | View:TenancyHistory | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/units/{unitId:guid}/ownership` | Create:OwnershipHistory | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/units/{unitId:guid}/tenancy` | Create:TenancyHistory | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/units/{unitId:guid}/tenancy/end` | Edit:TenancyHistory | ❌ | ❌ | ✅ | ✅ | ✅ |
| **Units** | | | | | | |
| `POST /api/v1/units` | Create:Unit | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units` | View:Unit | ✅ | ✅ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/{id:guid}` | View:Unit | ✅ | ✅ | ✅ | ✅ | ✅ |
| `PUT /api/v1/units/{id:guid}` | Edit:Unit | ❌ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/units/{id:guid}` | Delete:Unit | ❌ | ❌ | ✅ | ✅ | ✅ |
| `POST /api/v1/units/import` | Create:Unit | ❌ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/units/export` | Export:Unit | ❌ | ❌ | ✅ | ✅ | ✅ |
| **UserRoles** | | | | | | |
| `POST /api/v1/user-roles` | Assign:UserRole | ❌ | ❌ | ❌ | ❌ | ✅ |
| `DELETE /api/v1/user-roles` | Assign:UserRole | ❌ | ❌ | ❌ | ❌ | ✅ |
| `GET /api/v1/user-roles/{userId:guid}` | View:UserRole | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| **Users** | | | | | | |
| `POST /api/v1/users` | Create:User | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `GET /api/v1/users` | View:User | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| `GET /api/v1/users/{id:guid}` | View:User | ✅≈ | ✅≈ | ✅ | ✅ | ✅ |
| `PUT /api/v1/users/{id:guid}` | Edit:User | ✅≈ | ❌ | ✅ | ✅ | ✅ |
| `DELETE /api/v1/users/{id:guid}` | Delete:User | ❌ | ❌ | ✅ | ✅ | ✅ |
