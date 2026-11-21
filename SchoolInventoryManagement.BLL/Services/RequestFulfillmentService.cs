using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // Drives a request (ticket) through its lifecycle after the requester
    // is done:
    //
    //   Pending --approve--> InTransit --mark assigned--> Assigned --return--> Returned
    //   permanent Transfer:  ... --> Assigned --requester confirms--> Fulfilled
    //
    // A ticket has one line per model, each with an amount. Every unit that
    // goes out on it is one AssetAssignment carrying its RequestID.
    //
    // Approve  (any approver): staff pick exactly the right number of units
    //          for every line. Each is reserved to the requester (an open
    //          assignment, so nobody else can take it) and reads In transit.
    //          Borrow: set aside at the pickup location. Transfer: stays
    //          where it is until carried to the destination.
    // Mark assigned (Property Officer / Administrator): every unit on the
    //          ticket now reads Assigned. Borrow -- collected. Transfer --
    //          delivered, and recorded at the destination.
    // Confirm  (the requester): they received the units. Recorded on any
    //          request; on a permanent Transfer it is what hands the units
    //          to their department for good.
    // Return   (Property Officer / Administrator): some or all of the units
    //          come back to a location staff choose. The ticket is Returned
    //          when the last one is back.
    public class RequestFulfillmentService : IRequestFulfillmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAssetRequestService _requestService;
        private readonly IAssetAssignmentService _assignmentService;

        public RequestFulfillmentService(
            ApplicationDbContext context,
            IAssetRequestService requestService,
            IAssetAssignmentService assignmentService)
        {
            _context = context;
            _requestService = requestService;
            _assignmentService = assignmentService;
        }

        public async Task ApproveBorrowAsync(
            int requestId, List<int> assetIds, ConditionStatus? conditionOnAssignment,
            int departmentId, int pickupLocationId, byte[] requestRowVersion,
            int actingUserId, string? remarks)
        {
            var request = await _requestService.GetRequestByIdAsync(requestId);
            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestType != RequestType.Borrow)
                throw new InvalidOperationException("This is not a Borrow request.");

            var pickup = await _context.Locations.FindAsync(pickupLocationId);
            if (pickup is null)
                throw new KeyNotFoundException("Pickup location not found.");

            var units = await LoadAndCheckChosenUnitsAsync(requestId, assetIds, destinationLocationId: null);

            // Every step below shares this one context, so every
            // SaveChangesAsync inside the services joins this transaction:
            // all of it lands, or none of it does.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Approve first. The requester gets one combined message at
                // the end instead of one per step.
                await _requestService.ApproveRequestAsync(
                    requestId, requestRowVersion, actingUserId, notifyRequester: false);

                foreach (var unit in units)
                {
                    // AssignAssetAsync clears the location ("it's with a
                    // person now"), but it is not with them yet -- so
                    // remember where it was.
                    var origin = unit.CurrentLocationID;

                    // No condition given: record each unit as it stands.
                    await _assignmentService.AssignAssetAsync(
                        unit.AssetID,
                        request.RequestedByUser.UserID,
                        conditionOnAssignment ?? unit.Condition,
                        departmentId,
                        actingUserId,
                        remarks,
                        notifyRecipient: false,
                        requestId: requestId);

                    // Set aside at the pickup point, recorded as a movement
                    // from wherever it actually was. In transit until
                    // collected.
                    unit.CurrentLocationID = origin;
                    unit.Status = AssetStatus.InTransit;
                    MovementHelper.Record(
                        _context, unit, pickupLocationId, actingUserId,
                        SetAsideReason(requestId));
                }

                var requestRow = await _context.AssetRequests.FindAsync(requestId);
                requestRow!.PickupLocationID = pickupLocationId;
                requestRow.RequestStatus = RequestStatus.InTransit;

                NotificationHelper.Queue(
                    _context,
                    request.RequestedByUser.UserID,
                    $"Your Borrow request #{requestId} was approved — {request.ItemsSummary} " +
                    $"{(units.Count == 1 ? "is" : "are")} ready to collect at {pickup.LocationName}.",
                    $"/AssetRequests/Details/{requestId}");

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            // Audit logging happens on its own: AuditSaveChangesInterceptor
            // picks up every entity touched above as part of the same saves.
        }

        public async Task ApproveTransferAsync(
            int requestId, List<int> assetIds, ConditionStatus? conditionOnTransfer,
            byte[] requestRowVersion, int actingUserId, string? remarks)
        {
            var request = await _requestService.GetRequestByIdAsync(requestId);
            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestType != RequestType.Transfer)
                throw new InvalidOperationException("This is not a Transfer request.");

            // CK_AssetRequests_TypeFieldRules already guarantees this on a
            // Transfer row, but the service re-checks rather than
            // dereferencing a nullable on the strength of a DB constraint.
            if (request.RequestedLocationID is null)
                throw new ArgumentException("This Transfer request is missing its destination location.");

            var units = await LoadAndCheckChosenUnitsAsync(requestId, assetIds, request.RequestedLocationID);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await _requestService.ApproveRequestAsync(
                    requestId, requestRowVersion, actingUserId, notifyRequester: false);

                foreach (var unit in units)
                {
                    // Reserved to the requester so nobody else can take it.
                    // It stays physically where it is until delivered, so
                    // undo the location-clear AssignAssetAsync does.
                    var origin = unit.CurrentLocationID;

                    await _assignmentService.AssignAssetAsync(
                        unit.AssetID,
                        request.RequestedByUser.UserID,
                        conditionOnTransfer ?? unit.Condition,
                        request.DepartmentID,
                        actingUserId,
                        remarks,
                        notifyRecipient: false,
                        requestId: requestId);

                    unit.CurrentLocationID = origin;
                    unit.Status = AssetStatus.InTransit; // until it is delivered
                    if (conditionOnTransfer.HasValue)
                        unit.Condition = conditionOnTransfer.Value;
                }

                var requestRow = await _context.AssetRequests.FindAsync(requestId);
                requestRow!.RequestStatus = RequestStatus.InTransit;

                NotificationHelper.Queue(
                    _context,
                    request.RequestedByUser.UserID,
                    $"Your Transfer request #{requestId} was approved — {request.ItemsSummary} " +
                    $"{(units.Count == 1 ? "is" : "are")} on the way to {request.RequestedLocationName}.",
                    $"/AssetRequests/Details/{requestId}");

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // The units staff ticked must match the ticket exactly: for every
        // line, that many units of that model, all Available, none picked
        // twice, and nothing the ticket did not ask for. For a Transfer,
        // none may already be at the destination.
        private async Task<List<Asset>> LoadAndCheckChosenUnitsAsync(
            int requestId, List<int> assetIds, int? destinationLocationId)
        {
            var items = await _context.AssetRequestItems
                .Include(i => i.Model)
                .Where(i => i.RequestID == requestId)
                .ToListAsync();

            if (items.Count == 0)
                throw new InvalidOperationException("This request has no items on it.");

            var chosen = assetIds.Distinct().ToList();
            if (chosen.Count != assetIds.Count)
                throw new ArgumentException("The same unit was picked twice.");

            var units = await _context.Assets
                .Where(a => chosen.Contains(a.AssetID))
                .ToListAsync();

            if (units.Count != chosen.Count)
                throw new KeyNotFoundException("One of the chosen units no longer exists.");

            var notAvailable = units.FirstOrDefault(u => u.Status != AssetStatus.Available);
            if (notAvailable is not null)
                throw new InvalidOperationException(
                    $"{notAvailable.AssetCode} is now '{notAvailable.Status}' and cannot be handed out. Pick another unit.");

            if (destinationLocationId is not null)
            {
                var alreadyThere = units.FirstOrDefault(u => u.CurrentLocationID == destinationLocationId);
                if (alreadyThere is not null)
                    throw new InvalidOperationException(
                        $"{alreadyThere.AssetCode} is already at the destination. Pick another unit.");
            }

            var itemModelIds = items.Select(i => i.ModelID).ToHashSet();
            var stray = units.FirstOrDefault(u => !itemModelIds.Contains(u.ModelID));
            if (stray is not null)
                throw new ArgumentException($"{stray.AssetCode} is not one of the models on this request.");

            foreach (var item in items)
            {
                var picked = units.Count(u => u.ModelID == item.ModelID);
                if (picked != item.Quantity)
                    throw new ArgumentException(
                        $"{item.Model.ModelName}: pick {item.Quantity} unit{(item.Quantity == 1 ? "" : "s")} " +
                        $"(you picked {picked}).");
            }

            return units;
        }

        public async Task MarkAssignedAsync(int requestId, byte[] requestRowVersion, int actingUserId)
        {
            // Officers and Administrators only -- approving is wider (Principal
            // too), but handing over and recording returns is asset work.
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var request = await _context.AssetRequests
                .Include(r => r.RequestedLocation)
                .Include(r => r.Assignments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestStatus != RequestStatus.InTransit)
                throw new InvalidOperationException("Only a request that is ready for pickup or on its way can be marked picked up or delivered.");

            // A permanent delivery moves the unit into this room's branch.
            var destinationBranchId = request.RequestedLocation?.BranchID;

            var openAssignments = request.Assignments
                .Where(a => a.ReturnDate == null)
                .ToList();
            var openUnits = openAssignments.Select(a => a.Asset).ToList();

            if (openUnits.Count == 0)
                throw new InvalidOperationException("This request has no units out.");

            // A Transfer with no ReturnBy is permanent: once the requester
            // confirms receipt, the units belong to their department.
            var permanent = request.RequestType == RequestType.Transfer && request.ReturnBy is null;
            var now = PhilippineTime.Now;

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = requestRowVersion;

            foreach (var assignment in openAssignments)
            {
                var unit = assignment.Asset;

                if (request.RequestType == RequestType.Borrow)
                {
                    // Collected: with the requester now, not on a shelf --
                    // the same "no location" an ordinary assignment gives.
                    unit.CurrentLocationID = null;
                }
                else
                {
                    // Delivered: recorded at the destination, with a
                    // movement row so the history shows where it came from.
                    MovementHelper.Record(
                        _context, unit, request.RequestedLocationID!.Value, actingUserId,
                        permanent
                            ? $"Transfer request #{requestId}: delivered (permanent)"
                            : $"Transfer request #{requestId}: delivered");
                }

                // Delivered for good: the unit lives in the destination
                // room now, so its branch is that room's (the request form
                // keeps the room in the department's branch). It stays with
                // the requester until they confirm receipt
                // (AcknowledgeReceiptAsync); with no ReturnBy it can never
                // go overdue meanwhile.
                if (permanent && destinationBranchId is int branchId)
                    unit.BranchID = branchId;

                unit.Status = AssetStatus.Assigned;
            }

            // Every request waits at Assigned now. A permanent Transfer
            // finishes when the requester confirms receipt.
            request.RequestStatus = RequestStatus.Assigned;
            request.AssignedDate = now;

            var count = $"{openUnits.Count} item{(openUnits.Count == 1 ? "" : "s")}";
            var message = request.RequestType == RequestType.Borrow
                ? $"You collected {count} on request #{requestId}. Please confirm you received {(openUnits.Count == 1 ? "it" : "them")}."
                : $"{count} on request #{requestId} {(openUnits.Count == 1 ? "was" : "were")} delivered " +
                  $"to {request.RequestedLocation?.LocationName}{(permanent ? " for good" : "")}. " +
                  (permanent
                      ? $"Please confirm you received {(openUnits.Count == 1 ? "it" : "them")}: {(openUnits.Count == 1 ? "it" : "they")} then become your department's."
                      : $"Please confirm you received {(openUnits.Count == 1 ? "it" : "them")}.");

            NotificationHelper.Queue(
                _context, request.RequestedByUserID, message, $"/AssetRequests/Details/{requestId}");

            await SaveWithConcurrencyGuardAsync();
        }

        public async Task RecordReturnAsync(
            int requestId, List<int> assignmentIds, ConditionStatus conditionOnReturn,
            int? returnLocationId, byte[] requestRowVersion, int actingUserId,
            Dictionary<int, ConditionStatus>? unitConditions = null, string? remarks = null,
            DateTime? returnedOn = null)
        {
            remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
            if (remarks is { Length: > 500 })
                throw new ArgumentException("Remarks can be at most 500 characters.");
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var request = await _context.AssetRequests
                .Include(r => r.Assignments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            // Back to the Property Department of the borrower's branch, at its
            // room -- unless a room is given (older callers).
            returnLocationId ??= await ReturnRoomAsync(request.DepartmentID)
                ?? throw new InvalidOperationException(
                    "The Property Department of this branch has no room yet. Give it one on the Departments page first.");
            if (!await _context.Locations.AnyAsync(l => l.LocationID == returnLocationId))
                throw new KeyNotFoundException("Return location not found.");

            // Only what was actually picked up or delivered can come back.
            // An In Transit request never reached the requester -- it is
            // cancelled instead (CancelInTransitAsync).
            if (request.RequestStatus == RequestStatus.InTransit)
                throw new InvalidOperationException(
                    "Nothing on this request has been picked up or delivered yet, so there is nothing to return. Cancel the request instead.");

            if (request.RequestStatus != RequestStatus.Assigned)
                throw new InvalidOperationException("This request has nothing out to return.");

            // The borrower confirms they got the items before anything on
            // the request can be returned.
            if (request.AcknowledgedAt is null)
                throw new InvalidOperationException(
                    "The requester hasn't confirmed receiving the items yet. Record the return once they confirm.");

            // When the units actually came back: now, unless staff give an
            // earlier date and time (recorded late). Never in the future, and
            // never before they were handed over.
            var now = PhilippineTime.Now;
            var returnedAt = returnedOn ?? now;
            if (returnedAt > now.AddMinutes(5))
                throw new ArgumentException("Returned on can't be in the future.");
            if (request.AssignedDate is not null && returnedAt < request.AssignedDate.Value.AddMinutes(-1))
                throw new ArgumentException(
                    $"Returned on can't be before the items were handed over ({request.AssignedDate.Value:MMM d, yyyy h:mm tt}).");
            if (returnedAt > now) returnedAt = now;

            var open = request.Assignments.Where(a => a.ReturnDate == null).ToList();
            var returning = open.Where(a => assignmentIds.Contains(a.AssignmentID)).ToList();

            if (returning.Count == 0)
                throw new ArgumentException("Tick at least one unit that is coming back.");

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = requestRowVersion;

            foreach (var assignment in returning)
            {
                // Its own condition tag, or the one for every ticked unit.
                var condition = unitConditions is not null && unitConditions.TryGetValue(assignment.AssignmentID, out var tagged)
                    ? tagged : conditionOnReturn;
                assignment.ReturnDate = returnedAt;
                assignment.ConditionOnReturn = condition;
                if (remarks is not null)
                {
                    var note = "Returned: " + remarks;
                    assignment.Remarks = string.IsNullOrEmpty(assignment.Remarks) ? note : assignment.Remarks + " · " + note;
                    if (assignment.Remarks.Length > 500) assignment.Remarks = assignment.Remarks[..500];
                }

                var unit = assignment.Asset;
                unit.Condition = condition;

                // Back on a shelf, where staff said, and back in the
                // Property Department's stock.
                MovementHelper.Record(
                    _context, unit, returnLocationId.Value, actingUser.UserID,
                    $"Returned from request #{requestId}" + (remarks is null ? "" : $": {remarks}"), condition);
                await PropertyStock.ReturnToStockAsync(_context, unit);
            }

            var allBack = returning.Count == open.Count;
            if (allBack)
            {
                request.RequestStatus = RequestStatus.Returned;
                request.ReturnedDate = returnedAt;
            }
            else
            {
                // Touch the row so the concurrency check still applies to a
                // partial return -- two people returning the same units at
                // once should not both succeed.
                _context.Entry(request).Property(r => r.RequestStatus).IsModified = true;
            }

            var stillOut = open.Count - returning.Count;
            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                allBack
                    ? $"All items on request #{requestId} were returned, in {conditionOnReturn} condition."
                    : $"{returning.Count} item{(returning.Count == 1 ? "" : "s")} on request #{requestId} " +
                      $"returned; {stillOut} still out.",
                $"/AssetRequests/Details/{requestId}");

            await SaveWithConcurrencyGuardAsync();
        }

        // The Property Department room of a department's branch: its main
        // room, or else its first room. Null when it has none.
        public async Task<int?> ReturnRoomAsync(int departmentId)
        {
            var branchId = await _context.Departments.Where(d => d.DepartmentID == departmentId)
                .Select(d => (int?)d.BranchID).FirstOrDefaultAsync();
            var property = await _context.Departments.AsNoTracking()
                .Where(d => d.IsProperty && d.BranchID == branchId)
                .Select(d => new { d.DepartmentID, d.MainLocationID })
                .FirstOrDefaultAsync();
            if (property is null)
                return null;
            return property.MainLocationID
                ?? await _context.Locations.Where(l => l.DepartmentID == property.DepartmentID)
                    .OrderBy(l => l.LocationID).Select(l => (int?)l.LocationID).FirstOrDefaultAsync();
        }

        public async Task AcknowledgeReceiptAsync(int requestId, byte[] requestRowVersion, int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var request = await _context.AssetRequests
                .Include(r => r.Department)
                .Include(r => r.Assignments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestedByUserID != actingUserId)
                throw new UnauthorizedAccessException("Only the person who made the request can confirm receiving it.");

            if (request.AcknowledgedAt is not null)
                throw new InvalidOperationException("You already confirmed receiving this request.");

            if (request.RequestStatus != RequestStatus.Assigned)
                throw new InvalidOperationException(request.RequestStatus == RequestStatus.InTransit
                    ? "It has not been handed over yet. Confirm once you have it."
                    : "There is nothing on this request to confirm.");

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = requestRowVersion;

            var now = PhilippineTime.Now;
            request.AcknowledgedAt = now;
            request.AcknowledgedByUserID = actingUserId;

            var open = request.Assignments.Where(a => a.ReturnDate == null).ToList();
            var permanent = request.RequestType == RequestType.Transfer && request.ReturnBy is null;

            if (permanent)
            {
                // Handed over for good: the units are the department's now,
                // not the requester's. Closing the assignment takes them off
                // the requester's My Assets; the asset's history labels this
                // closing "Transferred permanently", not "Returned".
                foreach (var assignment in open)
                {
                    assignment.ReturnDate = now;
                    assignment.ConditionOnReturn = assignment.Asset.Condition;
                    await PropertyStock.IssueToDepartmentAsync(_context, assignment.Asset, request.DepartmentID);
                }

                // Nothing is out any more, so there is nothing to return.
                // Fulfilled is the status Request History counts as done.
                request.RequestStatus = RequestStatus.Fulfilled;
            }

            // Whoever approved it hears that it arrived.
            var who = $"{actor.FirstName} {actor.LastName}";
            if (request.ApprovedByUserID is int approverId && approverId != actingUserId)
                NotificationHelper.Queue(
                    _context, approverId,
                    permanent
                        ? $"{who} confirmed receiving request #{requestId}. The items are now {request.Department.DepartmentName}'s."
                        : $"{who} confirmed receiving the items on request #{requestId}.",
                    $"/AssetRequests/Details/{requestId}");

            await SaveWithConcurrencyGuardAsync();
        }

        // The movement written when a Borrow unit is set aside at the
        // pickup point. GetOriginalLocationsAsync finds it again by this
        // text, so both use the one wording.
        private static string SetAsideReason(int requestId) =>
            $"Borrow request #{requestId}: set aside for pickup";

        public async Task<Dictionary<int, int?>> GetOriginalLocationsAsync(int requestId)
        {
            var request = await _context.AssetRequests
                .Include(r => r.Assignments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            var units = request.Assignments
                .Where(a => a.ReturnDate == null)
                .Select(a => a.Asset)
                .ToList();
            var assetIds = units.Select(u => u.AssetID).ToList();

            var reason = SetAsideReason(requestId);
            var setAside = await _context.AssetMovements
                .Where(m => assetIds.Contains(m.AssetID) && m.ReasonForTransfer == reason)
                .OrderByDescending(m => m.MovementID)
                .Select(m => new { m.AssetID, m.SourceLocationID })
                .ToListAsync();

            // The set-aside movement says where each unit came from. No
            // movement means it was already at the pickup point when
            // approved, so where it is now is also where it was.
            return units.ToDictionary(
                u => u.AssetID,
                u => setAside.FirstOrDefault(m => m.AssetID == u.AssetID)?.SourceLocationID ?? u.CurrentLocationID);
        }

        public async Task CancelInTransitAsync(
            int requestId, string reason, IDictionary<int, int>? putBackLocations,
            byte[] requestRowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            reason = (reason ?? string.Empty).Trim();
            if (reason.Length == 0)
                throw new ArgumentException("Give a reason for cancelling.");
            if (reason.Length > 500)
                throw new ArgumentException("The reason is longer than 500 characters.");

            var request = await _context.AssetRequests
                .Include(r => r.Assignments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestStatus != RequestStatus.InTransit)
                throw new InvalidOperationException(
                    "Only a request that is ready for pickup or on its way can be cancelled this way. Once picked up or delivered, record a return instead.");

            var open = request.Assignments.Where(a => a.ReturnDate == null).ToList();
            var isBorrow = request.RequestType == RequestType.Borrow;

            // Borrow units were carried to the pickup point, so staff say
            // where each one was put back. Transfer units never moved.
            if (isBorrow)
            {
                putBackLocations ??= new Dictionary<int, int>();

                var missing = open.FirstOrDefault(a => !putBackLocations.ContainsKey(a.AssetID));
                if (missing is not null)
                    throw new ArgumentException($"Choose where {missing.Asset.AssetCode} was put back.");

                var chosen = open.Select(a => putBackLocations[a.AssetID]).Distinct().ToList();
                var found = await _context.Locations.CountAsync(l => chosen.Contains(l.LocationID));
                if (found != chosen.Count)
                    throw new KeyNotFoundException("One of the chosen locations no longer exists.");
            }

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = requestRowVersion;

            var now = PhilippineTime.Now;
            foreach (var assignment in open)
            {
                // Closed without ever being handed over, so the unit's
                // condition stays as it is.
                assignment.ReturnDate = now;
                assignment.ConditionOnReturn = assignment.Asset.Condition;

                if (isBorrow)
                    MovementHelper.Record(
                        _context, assignment.Asset, putBackLocations![assignment.AssetID], actingUserId,
                        $"Borrow request #{requestId} cancelled: put back");
                // Never handed over: straight back to the Property stock.
                await PropertyStock.ReturnToStockAsync(_context, assignment.Asset);
            }

            request.RequestStatus = RequestStatus.Cancelled;
            request.Remarks = reason;

            var before = request.RequestType == RequestType.Borrow ? "before pickup" : "before delivery";
            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your {request.RequestType} request #{requestId} was cancelled {before}. Reason: {reason}",
                $"/AssetRequests/Details/{requestId}");

            await SaveWithConcurrencyGuardAsync();
        }

        // Only units that were picked up can go overdue: an InTransit unit
        // never reached the requester, and a direct assignment has no
        // ReturnBy. Alerts go out on the Assigned -> Overdue flip, so each
        // request is announced once. If two page loads race, the second
        // save hits the asset RowVersion and is dropped, so nobody is
        // told twice.
        public async Task<int> MarkOverdueAsync()
        {
            var now = PhilippineTime.Now;

            var late = await _context.AssetAssignments
                .Include(a => a.Asset)
                .Include(a => a.Request!)
                    .ThenInclude(r => r.RequestedByUser)
                .Where(a => a.ReturnDate == null
                            && a.RequestID != null
                            && a.Request!.RequestStatus == RequestStatus.Assigned
                            && a.Request.ReturnBy < now
                            && a.Asset.Status == AssetStatus.Assigned)
                .ToListAsync();

            if (late.Count == 0)
                return 0;

            var managerIds = await _context.Users
                .Where(u => u.Status == "Active"
                            && (u.Role.RoleName == RoleNames.AssetOfficer
                                || u.Role.RoleName == RoleNames.Administrator))
                .Select(u => u.UserID)
                .ToListAsync();

            foreach (var group in late.GroupBy(a => a.RequestID!.Value))
            {
                var request = group.First().Request!;
                var due = request.ReturnBy!.Value.ToString("MMM d, h:mm tt");

                foreach (var assignment in group)
                {
                    assignment.Asset.Status = AssetStatus.Overdue;

                    // Written by hand, not by the audit interceptor: that
                    // one would credit whoever happened to load a page.
                    // This names the person holding the unit.
                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserID = assignment.AssignedToUserID,
                        ActionPerformed = "Asset Overdue",
                        TargetAssetID = assignment.AssetID,
                        EntityType = nameof(AssetRequest),
                        EntityID = request.RequestID,
                        LogDateTime = now,
                        Description = $"Not returned by {due} on request #{request.RequestID}. " +
                                      "Status: 'Assigned' -> 'Overdue' (set automatically)."
                    });
                }

                var units = group.Select(a => a.Asset).ToList();
                var what = units.Count == 1
                    ? $"{units[0].AssetName} ({units[0].AssetCode}) was"
                    : $"{units.Count} items were";
                var url = $"/AssetRequests/Details/{request.RequestID}";

                // The requester, plus whoever holds a unit if that is
                // someone else.
                var holderIds = group.Select(a => a.AssignedToUserID)
                    .Append(request.RequestedByUserID)
                    .Distinct()
                    .ToList();

                foreach (var userId in holderIds)
                    NotificationHelper.Queue(_context, userId,
                        $"Overdue: on your request #{request.RequestID}, {what} due back {due}. Please return it.",
                        url);

                // Everyone else in the department shares the items, so they
                // are told too (Scripts/029). Their link is My Assets: only
                // the requester sees the request itself.
                var requester = $"{request.RequestedByUser.FirstName} {request.RequestedByUser.LastName}";
                var memberIds = await _context.Users
                    .Where(u => u.Status == "Active" && u.DepartmentID == request.DepartmentID)
                    .Select(u => u.UserID)
                    .ToListAsync();
                foreach (var userId in memberIds.Except(holderIds))
                    NotificationHelper.Queue(_context, userId,
                        $"Overdue: {requester} borrowed {what.Replace(" was", "").Replace(" were", "")} for your department on request #{request.RequestID}, due back {due}. Please make sure it is returned.",
                        "/Assets/MyAssets");

                foreach (var userId in managerIds.Except(holderIds).Except(memberIds))
                    NotificationHelper.Queue(_context, userId,
                        $"Overdue: request #{request.RequestID} by {requester}: {what} due back {due}.",
                        url);
            }

            _context.SkipAutoAudit = true;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return 0;
            }
            finally
            {
                _context.SkipAutoAudit = false;
            }

            return late.Count;
        }

        private async Task SaveWithConcurrencyGuardAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This request was modified by someone else. Please reload and try again.");
            }
        }
    }
}