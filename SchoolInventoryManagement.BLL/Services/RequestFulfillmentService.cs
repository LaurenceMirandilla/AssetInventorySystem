using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // Drives a request through its lifecycle after the requester is done:
    //
    //   Pending --approve--> InTransit --mark assigned--> Assigned --return--> Returned
    //
    // Approve  (any approver): staff pick the unit, and it is reserved to
    //          the requester straight away (an open AssetAssignment, so
    //          nobody else can borrow it) and reads In transit on the Assets
    //          page. Borrow: it is set aside at the pickup location.
    //          Transfer: it stays where it is until it is carried to the
    //          destination.
    // Mark assigned (Asset Officer / Administrator): the unit now reads
    //          Assigned. Borrow -- the requester collected it, so it is with
    //          them now. Transfer -- it arrived, so it is recorded at the
    //          destination.
    // Return   (Asset Officer / Administrator): AssetAssignmentService.
    //          ReturnAssetAsync closes the assignment, puts the unit where
    //          staff say, and marks the request Returned.
    //
    // The request stays on the Approvals page from InTransit until Returned.
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

        public async Task ApproveAndAssignAsync(
            int requestId, int assetId, ConditionStatus conditionOnAssignment,
            int departmentId, int pickupLocationId, byte[] requestRowVersion,
            int actingUserId, string? remarks)
        {
            var request = await _requestService.GetRequestByIdAsync(requestId);
            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestType != RequestType.Borrow)
                throw new InvalidOperationException(
                    "This workflow only supports Borrow requests. Transfer requests use ApproveAndTransferAsync.");

            // The requester picked a Model, not a unit — so the unit staff
            // chose has to actually be one of that Model's units.
            var assetMatchesModel = await _context.Assets
                .AnyAsync(a => a.AssetID == assetId && a.ModelID == request.ModelID);
            if (!assetMatchesModel)
                throw new ArgumentException(
                    "The selected asset is not a unit of the model that was requested.");

            var pickup = await _context.Locations.FindAsync(pickupLocationId);
            if (pickup is null)
                throw new KeyNotFoundException("Pickup location not found.");

            // Every step below shares this one context, so every
            // SaveChangesAsync inside the services joins this transaction:
            // all of it lands, or none of it does.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Step 1: approve. The requester gets one combined message at
                // the end instead of one per step.
                await _requestService.ApproveRequestAsync(
                    requestId, requestRowVersion, actingUserId, notifyRequester: false);

                // Step 2: reserve the unit to the requester. AssignAssetAsync
                // clears the location ("it's with a person now"), but it is
                // not with them yet -- so remember where it was.
                var unit = await _context.Assets.FindAsync(assetId);
                if (unit is null)
                    throw new KeyNotFoundException("Asset not found.");
                var origin = unit.CurrentLocationID;

                await _assignmentService.AssignAssetAsync(
                    assetId,
                    request.RequestedByUser.UserID,
                    conditionOnAssignment,
                    departmentId,
                    actingUserId,
                    remarks,
                    notifyRecipient: false);

                // Step 3: set it aside at the pickup point, recorded as a
                // movement from wherever it actually was. It reads In transit
                // on the Assets page until it is collected.
                unit.CurrentLocationID = origin;
                unit.Status = AssetStatus.InTransit; // NEW
                MovementHelper.Record(
                    _context, unit, pickupLocationId, actingUserId,
                    $"Borrow request #{requestId}: set aside for pickup");

                // Step 4: the request is now InTransit -- approved, unit
                // chosen, waiting to be collected.
                var requestRow = await _context.AssetRequests.FindAsync(requestId);
                requestRow!.AssetID = assetId;
                requestRow.PickupLocationID = pickupLocationId;
                requestRow.RequestStatus = RequestStatus.InTransit;

                NotificationHelper.Queue(
                    _context,
                    request.RequestedByUser.UserID,
                    $"Your Borrow request was approved — {unit.AssetName} ({unit.AssetCode}) " +
                    $"is ready to collect at {pickup.LocationName}.",
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

        public async Task ApproveAndTransferAsync(
            int requestId, int? assetId, ConditionStatus? conditionOnTransfer,
            byte[] requestRowVersion, int actingUserId, string? remarks)
        {
            var request = await _requestService.GetRequestByIdAsync(requestId);
            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestType != RequestType.Transfer)
                throw new InvalidOperationException(
                    "This workflow only supports Transfer requests. Borrow requests use ApproveAndAssignAsync.");

            // CK_AssetRequests_TypeFieldRules already guarantees this on a
            // Transfer row, but the service re-checks rather than
            // dereferencing a nullable on the strength of a DB constraint.
            if (request.RequestedLocationID is null)
                throw new ArgumentException("This Transfer request is missing its destination location.");

            // Transfers name a Model, so staff pick the unit -- exactly as
            // for Borrow. A request made before that change already names
            // its unit; honour it rather than ask again.
            var unitId = request.AssetID ?? assetId;
            if (unitId is null)
                throw new ArgumentException("Choose which unit to transfer.");

            if (request.AssetID is null)
            {
                var unitMatchesModel = await _context.Assets
                    .AnyAsync(a => a.AssetID == unitId.Value && a.ModelID == request.ModelID);
                if (!unitMatchesModel)
                    throw new ArgumentException(
                        "The selected asset is not a unit of the model that was requested.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Step 1: approve (one combined message at the end).
                await _requestService.ApproveRequestAsync(
                    requestId, requestRowVersion, actingUserId, notifyRequester: false);

                // Step 2: reserve the unit to the requester so nobody else can
                // take it while it is out. It stays physically where it is
                // until staff mark it delivered, so undo the location-clear
                // AssignAssetAsync does.
                var unit = await _context.Assets.FindAsync(unitId.Value);
                if (unit is null)
                    throw new KeyNotFoundException("Asset not found.");
                var origin = unit.CurrentLocationID;

                await _assignmentService.AssignAssetAsync(
                    unitId.Value,
                    request.RequestedByUser.UserID,
                    conditionOnTransfer ?? unit.Condition,
                    request.DepartmentID,
                    actingUserId,
                    remarks,
                    notifyRecipient: false);

                unit.CurrentLocationID = origin;
                unit.Status = AssetStatus.InTransit; // NEW -- until it is delivered
                if (conditionOnTransfer.HasValue)
                    unit.Condition = conditionOnTransfer.Value;

                // Step 3: InTransit -- approved, unit chosen, on its way.
                var requestRow = await _context.AssetRequests.FindAsync(requestId);
                requestRow!.AssetID = unitId.Value;
                requestRow.RequestStatus = RequestStatus.InTransit;

                NotificationHelper.Queue(
                    _context,
                    request.RequestedByUser.UserID,
                    $"Your Transfer request was approved — {unit.AssetName} ({unit.AssetCode}) " +
                    $"is on its way to {request.RequestedLocationName}.",
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

        public async Task MarkAssignedAsync(int requestId, byte[] requestRowVersion, int actingUserId)
        {
            // Officers and Administrators only -- approving is wider (Principal
            // too), but handing over and recording returns is asset work.
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var request = await _context.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedLocation)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestStatus != RequestStatus.InTransit)
                throw new InvalidOperationException("Only a request that is In Transit can be marked Assigned.");

            if (request.Asset is null)
                throw new InvalidOperationException("This request has no unit recorded against it.");

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = requestRowVersion;

            string message;
            if (request.RequestType == RequestType.Borrow)
            {
                // Collected: it is with the requester now, not on a shelf --
                // the same "no location" an ordinary assignment gives it.
                request.Asset.CurrentLocationID = null;
                message = $"You collected {request.Asset.AssetName} ({request.Asset.AssetCode}).";
            }
            else
            {
                // Delivered: recorded at the destination, with a movement row
                // so the unit's history shows where it came from.
                MovementHelper.Record(
                    _context, request.Asset, request.RequestedLocationID!.Value, actingUserId,
                    $"Transfer request #{requestId}: delivered");
                message = $"{request.Asset.AssetName} ({request.Asset.AssetCode}) was delivered " +
                          $"to {request.RequestedLocation?.LocationName}.";
            }

            // NEW -- the unit's own status follows the request's: no longer
            // in transit, now with the requester (or at the destination).
            request.Asset.Status = AssetStatus.Assigned;

            request.RequestStatus = RequestStatus.Assigned;
            request.AssignedDate = DateTime.Now;

            NotificationHelper.Queue(
                _context, request.RequestedByUserID, message, $"/AssetRequests/Details/{requestId}");

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