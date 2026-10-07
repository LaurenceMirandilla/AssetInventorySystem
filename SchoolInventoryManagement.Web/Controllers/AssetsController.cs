using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using QRCoder;

namespace SchoolInventoryManagement.Web.Controllers
{
    [Authorize] // any logged-in user can view; specific actions below tighten further
    public class AssetsController : BaseController
    {
        private readonly IAssetAssignmentService _assignmentService;
        private readonly IAssetMovementService _movementService;
        private readonly IAssetService _assetService;
        private readonly ApplicationDbContext _context; // dropdown lookups and simple counts
        private readonly IDisposalService _disposalService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        // Matches the mockup's page size. Unrelated to the KPI tiles above
        // it -- those count the whole filtered set, not just this page.
        private const int PageSize = 20;

        public AssetsController(
            IAssetService assetService,
            IDisposalService disposalService,
            IAssetAssignmentService assignmentService,
            IAssetMovementService movementService,
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment)
        {
            _assetService = assetService;
            _disposalService = disposalService;
            _assignmentService = assignmentService;
            _movementService = movementService;
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Teachers and Staff see the catalogue of models, not the units:
        // no counts, codes, statuses or locations. The one exception is a
        // unit assigned (or reserved) to them, which they can open from
        // My Assets, their requests and their notifications.
        private bool IsRequesterOnly =>
            User.IsInRole(RoleNames.Teacher) || User.IsInRole(RoleNames.Staff);

        private bool CanOpenUnit(AssetResponseDTO asset) =>
            !IsRequesterOnly || asset.AssignedUserID == CurrentUserId;

        private async Task PopulateDropdownsAsync()
        {
            ViewBag.Models = new SelectList(
                await _context.Models.OrderBy(m => m.ModelName).ToListAsync(),
                "ModelID", "ModelName");

            // For the register forms' type-to-search model box: each model
            // with its category's code prefix and the next number free, so
            // the form can show the code(s) about to be created.
            var nextNumbers = await _assetService.GetNextCodeNumbersAsync();
            var modelChoices = await _context.Models
                .OrderBy(m => m.ModelName)
                .Select(m => new ModelCodeOption
                {
                    ModelID = m.ModelID,
                    ModelName = m.ModelName,
                    CategoryName = m.Category.CategoryName,
                    CodePrefix = m.Category.CodePrefix
                })
                .ToListAsync();
            foreach (var choice in modelChoices)
                choice.NextNumber = nextNumbers.GetValueOrDefault(choice.CodePrefix, 1);
            ViewBag.ModelChoices = modelChoices;

            ViewBag.Locations = new SelectList(
                await _context.Locations.OrderBy(l => l.LocationName).ToListAsync(),
                "LocationID", "LocationName");

            ViewBag.Departments = new SelectList(
                await _context.Departments.OrderBy(d => d.DepartmentName).ToListAsync(),
                "DepartmentID", "DepartmentName");

            ViewBag.Branches = new SelectList(
                await _context.Branches.OrderBy(b => b.BranchName).ToListAsync(),
                "BranchID", "BranchName");

            ViewBag.LocationsByBranch = await _context.Locations
                .OrderBy(l => l.LocationName)
                .Select(l => new LocationDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    Description = l.Description,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName
                })
                .ToListAsync();

            ViewBag.DepartmentsByBranch = await _context.Departments
                .OrderBy(d => d.DepartmentName)
                .Select(d => new DepartmentDTO
                {
                    DepartmentID = d.DepartmentID,
                    DepartmentName = d.DepartmentName,
                    Description = d.Description,
                    BranchID = d.BranchID,
                    BranchName = d.Branch.BranchName
                })
                .ToListAsync();

            // For the "+ New model" popup's category list. Categories with
            // no code prefix are left out -- no asset could be registered
            // under them anyway.
            ViewBag.QuickAddCategories = await _context.Categories
                .Where(c => c.CodePrefix != null && c.CodePrefix != "")
                .OrderBy(c => c.CategoryName)
                .Select(c => new CategoryDTO
                {
                    CategoryID = c.CategoryID,
                    CategoryName = c.CategoryName,
                    CodePrefix = c.CodePrefix
                })
                .ToListAsync();
        }

        // Feeds the Index page's filter row -- separate from
        // PopulateDropdownsAsync above, which feeds the Create/Edit forms.
        // Every child list carries its parent's id so the page can cascade:
        // Category narrows Model, Branch narrows Location and Department.
        // The narrowing itself is JavaScript in Index.cshtml; this only
        // ships the relationships. Grouped by parent so the lists stay
        // readable when nothing is narrowed yet.
        private async Task PopulateFilterOptionsAsync(AssetIndexViewModel model)
        {
            model.CategoryOptions = await _context.Categories
                .OrderBy(c => c.CategoryName)
                .Select(c => new CategoryDTO { CategoryID = c.CategoryID, CategoryName = c.CategoryName })
                .ToListAsync();

            model.ModelOptions = await _context.Models
                .OrderBy(m => m.Category.CategoryName).ThenBy(m => m.ModelName)
                .Select(m => new ModelDTO
                {
                    ModelID = m.ModelID,
                    ModelName = m.ModelName,
                    CategoryID = m.CategoryID,
                    CategoryName = m.Category.CategoryName
                })
                .ToListAsync();

            model.BranchOptions = await _context.Branches
                .OrderBy(b => b.BranchName)
                .Select(b => new BranchDTO { BranchID = b.BranchID, BranchName = b.BranchName })
                .ToListAsync();

            model.LocationOptions = await _context.Locations
                .OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                .Select(l => new LocationDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName
                })
                .ToListAsync();

            model.DepartmentOptions = await _context.Departments
                .OrderBy(d => d.Branch.BranchName).ThenBy(d => d.DepartmentName)
                .Select(d => new DepartmentDTO
                {
                    DepartmentID = d.DepartmentID,
                    DepartmentName = d.DepartmentName,
                    BranchID = d.BranchID,
                    BranchName = d.Branch.BranchName
                })
                .ToListAsync();

            ViewBag.StatusFilter = new SelectList(
                Enum.GetValues(typeof(AssetStatus)).Cast<AssetStatus>()
                    .Select(s => new { Value = s.ToString(), Text = s.ToString() }),
                "Value", "Text", model.Status?.ToString());

            ViewBag.ConditionFilter = new SelectList(
                Enum.GetValues(typeof(ConditionStatus)).Cast<ConditionStatus>()
                    .Select(c => new { Value = c.ToString(), Text = c.ToString() }),
                "Value", "Text", model.Condition?.ToString());
        }

        // GET /Assets
        public async Task<IActionResult> Index(
            string? keyword, int? categoryId, int? departmentId,
            AssetStatus? status, ConditionStatus? condition, int? locationId, int? modelId,
            int? branchId, bool includeDisposed = false, int page = 1)
        {
            if (IsRequesterOnly)
                return await CatalogAsync(keyword, categoryId);

            // One query drives both the table and the KPI tiles, which is
            // what makes the tiles mirror the rows: every filter, status
            // included, narrows this set, and the tiles are just its
            // breakdown. With a status selected the others read zero.
            var matches = await _assetService.SearchAssetsAsync(
                keyword, categoryId, modelId, branchId, departmentId, status, condition, locationId);

            // Disposed units are hidden unless asked for. Done here, not in
            // SearchAssetsAsync, because the request screens and QR scan use
            // that too and must keep finding every unit. The Disposed tile
            // still counts them, so it says how many are hidden.
            var showDisposed = includeDisposed || status == AssetStatus.Disposed;
            var filtered = showDisposed
                ? matches
                : matches.Where(a => a.Status != AssetStatus.Disposed).ToList();

            var currentPage = Math.Max(page, 1);
            var totalFiltered = filtered.Count;

            var paged = filtered
                .Skip((currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            // Unfiltered headcount, for the "N of M" caption above the tiles.
            // A COUNT(*) rather than another GetAllAssetsAsync -- the page
            // needs the number, not the rows. Leaves out disposed units when
            // the list does, so the two numbers describe the same set.
            var grandTotal = showDisposed
                ? await _context.Assets.CountAsync()
                : await _context.Assets.CountAsync(a => a.Status != AssetStatus.Disposed);

            var model = new AssetIndexViewModel
            {
                Assets = paged,
                AvailableCount = filtered.Count(a => a.Status == AssetStatus.Available),
                AssignedCount = filtered.Count(a => a.Status == AssetStatus.Assigned),
                InTransitCount = filtered.Count(a => a.Status == AssetStatus.InTransit),
                OverdueCount = filtered.Count(a => a.Status == AssetStatus.Overdue),
                UnderMaintenanceCount = filtered.Count(a => a.Status == AssetStatus.UnderMaintenance),
                DisposedCount = matches.Count(a => a.Status == AssetStatus.Disposed),
                GrandTotalCount = grandTotal,
                IncludeDisposed = includeDisposed,
                Keyword = keyword,
                CategoryId = categoryId,
                DepartmentId = departmentId,
                Status = status,
                Condition = condition,
                LocationId = locationId,
                ModelId = modelId,
                BranchId = branchId,
                Page = currentPage,
                PageSize = PageSize,
                TotalFilteredCount = totalFiltered
            };

            await PopulateFilterOptionsAsync(model);

            return View(model);
        }

        // GET /Assets/ExportAssets -- same filters as Index, no paging.
        public async Task<IActionResult> ExportAssets(
            string? keyword, int? categoryId, int? departmentId,
            AssetStatus? status, ConditionStatus? condition, int? locationId, int? modelId,
            int? branchId, bool includeDisposed = false)
        {
            if (IsRequesterOnly)
                return Forbid();

            var assets = await _assetService.SearchAssetsAsync(
                keyword, categoryId, modelId, branchId, departmentId, status, condition, locationId);

            // Same rule as the list, so the file matches what was on screen.
            if (!includeDisposed && status != AssetStatus.Disposed)
                assets = assets.Where(a => a.Status != AssetStatus.Disposed).ToList();

            var csv = CsvExportHelper.Build(
                new[]
                {
                    "Code", "Name", "Model", "Category", "Status", "Condition",
                    "Location", "Holder", "Department", "Branch", "Acquisition Cost (PHP)"
                },
                assets.Select(a => new string?[]
                {
                    a.AssetCode,
                    a.AssetName,
                    a.ModelName,
                    a.CategoryName,
                    a.Status.ToString(),
                    a.Condition.ToString(),
                    a.CurrentLocationName,
                    a.AssignedUserName,
                    a.DepartmentName,
                    a.BranchName,
                    a.AcquisitionCost?.ToString("0.00", CultureInfo.InvariantCulture)
                }));

            var filters = Request.QueryString.HasValue
                ? " " + System.Net.WebUtility.UrlDecode(Request.QueryString.Value)
                : "";
            await RecordAccessAsync("Data Exported", $"Assets list{filters}");

            return File(csv, "text/csv", CsvExportHelper.TimestampedFileName("assets"));
        }

        // GET /Assets/MyAssets
        //
        // The only view of AssetAssignments a non-manager can reach. It is
        // hard-wired to CurrentUserId rather than taking an id, because
        // GetActiveAssignmentsForUserAsync does no permission check of its
        // own -- an id parameter here would let any signed-in user read
        // anybody else's open assignments.
        public async Task<IActionResult> MyAssets()
        {
            var assignments = await _assignmentService.GetActiveAssignmentsForUserAsync(CurrentUserId);
            return View(assignments);
        }

        // GET /Assets/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            if (!CanOpenUnit(asset))
            {
                TempData["ErrorMessage"] = "You can only open items that are assigned to you. See My Assets.";
                return RedirectToAction(nameof(Index));
            }

            return View(asset);
        }

        // The Assets page for Teachers and Staff: every model, with its
        // category and description, and nothing about the units. They
        // request a model; the approver picks the unit.
        private async Task<IActionResult> CatalogAsync(string? keyword, int? categoryId)
        {
            var query = _context.Models.AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var term = keyword.Trim();
                query = query.Where(m =>
                    m.ModelName.Contains(term) ||
                    m.Category.CategoryName.Contains(term) ||
                    (m.Description != null && m.Description.Contains(term)));
            }

            if (categoryId.HasValue)
                query = query.Where(m => m.CategoryID == categoryId.Value);

            var model = new AssetCatalogViewModel
            {
                Keyword = keyword,
                CategoryId = categoryId,
                Models = await query
                    .OrderBy(m => m.Category.CategoryName).ThenBy(m => m.ModelName)
                    .Select(m => new ModelDTO
                    {
                        ModelID = m.ModelID,
                        ModelName = m.ModelName,
                        CategoryID = m.CategoryID,
                        CategoryName = m.Category.CategoryName,
                        Description = m.Description
                    })
                    .ToListAsync(),
                CategoryOptions = await _context.Categories
                    .OrderBy(c => c.CategoryName)
                    .Select(c => new CategoryDTO { CategoryID = c.CategoryID, CategoryName = c.CategoryName })
                    .ToListAsync()
            };

            return View("Catalog", model);
        }

        // GET /Assets/Create
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Create()
        {
            await PopulateDropdownsAsync();
            return View(new AssetCreateViewModel());
        }

        // POST /Assets/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        [RequestSizeLimit(FileUploads.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = FileUploads.MaxRequestBytes)]
        public async Task<IActionResult> Create(AssetCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            try
            {
                // Both files are checked before either is saved, so a bad
                // second file does not leave the first one behind.
                FileUploads.Validate(model.ImageFile, FileUploads.ImageExtensions, "photo");
                FileUploads.Validate(model.WarrantyFile, FileUploads.DocumentExtensions, "warranty file");

                var savedPath = await FileUploads.SaveAsync(model.ImageFile, _webHostEnvironment.WebRootPath, PhotoFolder);
                var warrantyPath = await FileUploads.SaveAsync(model.WarrantyFile, _webHostEnvironment.WebRootPath, WarrantyFolder);
                var dto = new CreateAssetDTO
                {
                    ModelID = model.ModelID!.Value,
                    AssetName = model.AssetName,
                    Description = model.Description,
                    SerialNumber = model.SerialNumber,
                    AcquisitionDate = model.AcquisitionDate,
                    AcquisitionCost = model.AcquisitionCost,
                    WarrantyInformation = model.WarrantyInformation,
                    WarrantyFileURL = warrantyPath,
                    ImageURL = savedPath,
                    CreateQr = model.CreateQr,
                    Condition = model.Condition,
                    CurrentLocationID = model.CurrentLocationID,
                    BranchID = model.BranchID,
                    DepartmentID = model.DepartmentID!.Value
                };

                var created = await _assetService.CreateAssetAsync(dto, CurrentUserId);
                TempData["StatusMessage"] = $"Registered {created.AssetCode}.";
                return RedirectToAction(nameof(Details), new { id = created.AssetID });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync();
                return View(model);
            }
        }

        // GET /Assets/BulkCreate
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> BulkCreate()
        {
            await PopulateDropdownsAsync();
            return View(new AssetBulkCreateViewModel());
        }

        // POST /Assets/BulkCreate
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        [RequestSizeLimit(FileUploads.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = FileUploads.MaxRequestBytes)]
        public async Task<IActionResult> BulkCreate(AssetBulkCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            try
            {
                FileUploads.Validate(model.WarrantyFile, FileUploads.DocumentExtensions, "warranty file");
                var warrantyPath = await FileUploads.SaveAsync(model.WarrantyFile, _webHostEnvironment.WebRootPath, WarrantyFolder);

                var dto = new BulkCreateAssetsDTO
                {
                    ModelID = model.ModelID!.Value,
                    Quantity = model.Quantity,
                    BaseName = model.BaseName,
                    SerialNumbers = model.SerialNumbers,
                    Description = model.Description,
                    AcquisitionDate = model.AcquisitionDate,
                    AcquisitionCost = model.AcquisitionCost,
                    WarrantyInformation = model.WarrantyInformation,
                    WarrantyFileURL = warrantyPath,
                    Condition = model.Condition,
                    CurrentLocationID = model.CurrentLocationID,
                    BranchID = model.BranchID!.Value,
                    DepartmentID = model.DepartmentID!.Value,
                    CreateQr = model.CreateQr
                };

                var codes = await _assetService.BulkCreateAssetsAsync(dto, CurrentUserId);

                TempData["StatusMessage"] = codes.Count == 1
                    ? $"Registered 1 asset: {codes[0]}."
                    : $"Registered {codes.Count} assets: {codes[0]} to {codes[^1]}.";

                // Land on the list filtered to that model, so the new batch
                // is right there.
                return RedirectToAction(nameof(Index), new { modelId = model.ModelID });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync();
                return View(model);
            }
        }

        // GET /Assets/Edit/5
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Edit(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            // Out on a request -- reserved and waiting (InTransit) or with
            // someone (Assigned, or Overdue once late). Editing its location
            // now would pull it out from under the request.
            if (asset.Status == AssetStatus.Assigned || asset.Status == AssetStatus.InTransit ||
                asset.Status == AssetStatus.Overdue)
            {
                TempData["ErrorMessage"] = "This asset is out on a request and cannot be edited until it's returned.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await PopulateDropdownsAsync();

            var model = new AssetEditViewModel
            {
                AssetID = asset.AssetID,
                AssetName = asset.AssetName,
                Description = asset.Description,
                SerialNumber = asset.SerialNumber,
                Condition = asset.Condition,
                AcquisitionCost = asset.AcquisitionCost,
                WarrantyInformation = asset.WarrantyInformation,
                WarrantyFileURL = asset.WarrantyFileURL,
                ImageURL = asset.ImageURL,
                CurrentLocationID = asset.CurrentLocationID,
                BranchID = asset.BranchID,
                DepartmentID = asset.DepartmentID,
                RowVersionBase64 = RowVersionHelper.ToBase64(asset.RowVersion)
            };

            return View(model);
        }

        // POST /Assets/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        [RequestSizeLimit(FileUploads.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = FileUploads.MaxRequestBytes)]
        public async Task<IActionResult> Edit(int id, AssetEditViewModel model)
        {
            // The current photo and warranty file come from the database,
            // never from the form (see [BindNever] on the view model).
            var current = await _assetService.GetAssetByIdAsync(id);
            if (current is null)
                return NotFound();

            model.AssetID = id;
            model.ImageURL = current.ImageURL;
            model.WarrantyFileURL = current.WarrantyFileURL;

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            try
            {
                FileUploads.Validate(model.ImageFile, FileUploads.ImageExtensions, "photo");
                FileUploads.Validate(model.WarrantyFile, FileUploads.DocumentExtensions, "warranty file");

                var newPath = await FileUploads.SaveAsync(model.ImageFile, _webHostEnvironment.WebRootPath, PhotoFolder);
                var newWarrantyPath = await FileUploads.SaveAsync(model.WarrantyFile, _webHostEnvironment.WebRootPath, WarrantyFolder);

                // A new file replaces the old one; "remove" with no new file
                // clears it; otherwise the current one stays.
                var warrantyFileUrl = newWarrantyPath
                    ?? (model.RemoveWarrantyFile ? null : model.WarrantyFileURL);

                var dto = new UpdateAssetDTO
                {
                    AssetName = model.AssetName,
                    Description = model.Description,
                    SerialNumber = model.SerialNumber,
                    Condition = model.Condition,
                    AcquisitionCost = model.AcquisitionCost,
                    WarrantyInformation = model.WarrantyInformation,
                    WarrantyFileURL = warrantyFileUrl,
                    ImageURL = newPath ?? model.ImageURL,
                    CurrentLocationID = model.CurrentLocationID,
                    BranchID = model.BranchID,
                    DepartmentID = model.DepartmentID,
                    RowVersion = RowVersionHelper.FromBase64(model.RowVersionBase64)
                };

                await _assetService.UpdateAssetAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync();
                return View(model);
            }
        }

        // GET /Assets/Dispose/5
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Dispose(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            var model = new DisposeAssetViewModel
            {
                AssetID = asset.AssetID,
                AssetRowVersionBase64 = RowVersionHelper.ToBase64(asset.RowVersion)
            };

            ViewBag.AssetName = asset.AssetName;
            ViewBag.AssetCode = asset.AssetCode;
            return View(model);
        }

        // POST /Assets/Dispose/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Dispose(int id, DisposeAssetViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var dto = new DisposeAssetDTO
                {
                    ReasonForDisposal = model.ReasonForDisposal,
                    DisposalMethod = model.DisposalMethod,
                    SupportingDocumentationURL = model.SupportingDocumentationURL,
                    Notes = model.Notes,
                    AssetRowVersion = RowVersionHelper.FromBase64(model.AssetRowVersionBase64)
                };

                await _disposalService.DisposeAssetAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(model);
            }
        }

        // POST /Assets/Restore/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Restore(int id, string rowVersionBase64)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _disposalService.RestoreAssetAsync(id, rowVersion, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET /Assets/History/5
        //
        // The audit trail names who did what and when, so it is management
        // only -- the same trio that can reach Reports. Note this is the
        // only gate on it: the action pulls AuditLogs straight from the
        // context rather than through a service that checks permissions.
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal)]
        public async Task<IActionResult> History(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            var model = new AssetHistoryViewModel
            {
                Asset = asset,
                Assignments = await _assignmentService.GetAssignmentHistoryForAssetAsync(id),
                Movements = await _movementService.GetMovementHistoryForAssetAsync(id),
                Disposals = await _disposalService.GetDisposalHistoryForAssetAsync(id),
                AuditEntries = await _context.AuditLogs
                    .Where(a => a.TargetAssetID == id)
                    .OrderByDescending(a => a.LogDateTime)
                    .ToListAsync()
            };

            return View(model);
        }

        // Where uploads are kept, under wwwroot. Size and type rules are in
        // Helpers/FileUploads (3 MB limit).
        private const string PhotoFolder = "images/assets";
        private const string WarrantyFolder = "uploads/warranty";

        // GET /Assets/QrCode/5 -- draws the PNG on demand from the code
        // stored when the QR was created. Nothing stored means no QR.
        public async Task<IActionResult> QrCode(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null || string.IsNullOrEmpty(asset.QRCodeData) || !CanOpenUnit(asset))
                return NotFound();

            var scanUrl = Url.Action(nameof(Scan), "Assets",
                new { code = asset.QRCodeData }, Request.Scheme)!;

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(scanUrl, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(20);

            return File(png, "image/png");
        }

        // POST /Assets/CreateQr/5 -- the Details page's "Create QR" button,
        // for an asset that was registered without one.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> CreateQr(int id)
        {
            try
            {
                await _assetService.CreateQrCodeAsync(id, CurrentUserId);
                TempData["StatusMessage"] = "QR code created.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET /Assets/Scan?code=AST-0001
        //
        // Only opens an asset whose QR was created (Add QR on the register
        // forms, or Create QR on its page). An asset without one is turned
        // away, even though its code exists.
        public async Task<IActionResult> Scan(string code)
        {
            var results = await _assetService.SearchAssetsAsync(
                code, null, null, null, null, null, null);
            var asset = results.FirstOrDefault(a => a.AssetCode == code);

            if (asset is null)
            {
                TempData["ErrorMessage"] = $"No asset found for code '{code}'.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrEmpty(asset.QRCodeData))
            {
                TempData["ErrorMessage"] = $"{code} has no QR code. Open the asset and click Create QR to make one.";
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Details), new { id = asset.AssetID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> SendToMaintenance(int id, string rowVersionBase64)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _assetService.ChangeStatusAsync(id, AssetStatus.UnderMaintenance, rowVersion, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST /Assets/ReturnToService/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> ReturnToService(int id, string rowVersionBase64)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _assetService.ReturnFromMaintenanceAsync(id, rowVersion, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }
    }
}