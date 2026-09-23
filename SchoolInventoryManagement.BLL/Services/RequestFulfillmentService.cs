using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    public class RequestFulfillmentService : IRequestFulfillmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAssetRequestService _requestService;
        private readonly IAssetAssignmentService _assignmentService;
        private readonly IAssetMovementService _movementService;

        public RequestFulfillmentService(
            ApplicationDbContext context,
            IAssetRequestService requestService,
            IAssetAssignmentService assignmentService,
            IAssetMovementService movementService)
        {
            _context = context;
            _requestService = requestService;
            _assignmentService = assignmentService;
            _movementService = movementService;
        }

        public async Task ApproveAndAssignAsync(
            int requestId, int assetId, ConditionStatus conditionOnAssignment,
            int departmentId, byte[] requestRowVersion, int actingUserId, string? remarks)
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

            // All three steps below share the SAME ApplicationDbContext instance
            // (injected once, scoped per HTTP request). Beginning a transaction
            // here means every SaveChangesAsync() call made by the services
            // below participates in this one transaction — none of them commit
            // independently until we call CommitAsync() at the end.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Step 1: Approve the request.
                // notifyRequester: false throughout this method — approving,
                // assigning and fulfilling all happen in one action here, and
                // three separate notifications for one click is noise. One
                // combined message is sent at the end instead.
                await _requestService.ApproveRequestAsync(
                    requestId, requestRowVersion, actingUserId, notifyRequester: false);

                // RowVersion changed after the approve save above — re-fetch
                // the request to get its current RowVersion before the next
                // concurrency-checked call (Fulfill, later in this method).
                var approvedRequest = await _requestService.GetRequestByIdAsync(requestId);
                if (approvedRequest is null)
                    throw new KeyNotFoundException("Request not found after approval.");

                // Step 2: Create the assignment (also updates Asset.Status -> Assigned)
                await _assignmentService.AssignAssetAsync(
                    assetId,
                    approvedRequest.RequestedByUser.UserID,
                    conditionOnAssignment,
                    departmentId,
                    actingUserId,
                    remarks,
                    notifyRecipient: false);

                // Step 3: Mark the request as Fulfilled
                await _requestService.FulfillRequestAsync(
                    requestId,
                    approvedRequest.RowVersion,
                    actingUserId,
                    notifyRequester: false);

                // Step 4: one message covering the whole outcome. Queued
                // INSIDE the transaction, so a rollback takes it with
                // everything else rather than announcing an approval that
                // never happened.
                var assignedAsset = await _context.Assets.FindAsync(assetId);

                NotificationHelper.Queue(
                    _context,
                    approvedRequest.RequestedByUser.UserID,
                    $"Your Borrow request was approved — {assignedAsset!.AssetName} " +
                    $"({assignedAsset.AssetCode}) has been assigned to you.",
                    $"/AssetRequests/Details/{requestId}");

                await _context.SaveChangesAsync();

                // All steps succeeded — commit everything together
                await transaction.CommitAsync();
            }
            catch
            {
                // Any failure above rolls back ALL changes made within this
                // transaction, including the Approve step, even though it
                // already called SaveChangesAsync() internally.
                await transaction.RollbackAsync();
                throw;
            }

            // Audit logging happens on its own: AuditSaveChangesInterceptor
            // picks up every entity touched above as part of the same save.
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

            // Transfers name a Model now, so staff pick the unit -- exactly
            // as for Borrow. A request made before that change already names
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

            // Same single-transaction shape as ApproveAndAssignAsync above:
            // Approve, move, and Fulfill either all land or none do.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Step 1: Approve the request (see the Borrow flow above for
                // why notifications are suppressed step by step here).
                await _requestService.ApproveRequestAsync(
                    requestId, requestRowVersion, actingUserId, notifyRequester: false);

                // The approve save above refreshed RowVersion, and Fulfill
                // checks it again — so re-read before using it.
                var approvedRequest = await _requestService.GetRequestByIdAsync(requestId);
                if (approvedRequest is null)
                    throw new KeyNotFoundException("Request not found after approval.");

                // Step 2: Move the asset (also updates Asset.CurrentLocationID)
                await _movementService.TransferAssetAsync(
                    unitId.Value,
                    request.RequestedLocationID.Value,
                    BuildTransferReason(request.RequestID, request.Reason),
                    conditionOnTransfer,
                    actingUserId,
                    remarks);

                // Step 3: Mark the request as Fulfilled
                await _requestService.FulfillRequestAsync(
                    requestId,
                    approvedRequest.RowVersion,
                    actingUserId,
                    notifyRequester: false);

                // Step 4: record which unit went, so the request's Details
                // page can show it. The tracked entity already carries the
                // RowVersion from Fulfill's save, and this rides along in the
                // same final SaveChangesAsync as the notification below.
                var requestRow = await _context.AssetRequests.FindAsync(requestId);
                requestRow!.AssetID = unitId.Value;

                // Step 5: one message covering the whole outcome.
                var movedAsset = await _context.Assets.FindAsync(unitId.Value);

                NotificationHelper.Queue(
                    _context,
                    approvedRequest.RequestedByUser.UserID,
                    $"Your Transfer request was approved — {movedAsset!.AssetName} " +
                    $"({movedAsset.AssetCode}) has been moved.",
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

        // AssetMovements.ReasonForTransfer is NOT NULL VARCHAR(500) while a
        // request's Reason is optional — so fall back to naming the request
        // the movement came from, and keep the result inside the column.
        private static string BuildTransferReason(int requestId, string? requestReason)
        {
            var reason = string.IsNullOrWhiteSpace(requestReason)
                ? $"Transfer request #{requestId}"
                : $"Transfer request #{requestId}: {requestReason}";

            return reason.Length > 500 ? reason[..500] : reason;
        }
    }
}