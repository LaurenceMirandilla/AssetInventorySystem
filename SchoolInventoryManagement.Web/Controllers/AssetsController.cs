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
using System.Text;
using System.Threading.Tasks;

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
        private readonly IAssetCountService _countService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        // Matches the mockup's default page size. The footer's "per page"
        // picker can choose any of PageSizeOptions; anything else falls
        // back to this.
        private const int PageSize = 20;
        private static readonly int[] PageSizeOptions = { 10, 20, 50, 100 };

        public AssetsController(
            IAssetService assetService,
            IDisposalService disposalService,
            IAssetAssignmentService assignmentService,
            IAssetMovementService movementService,
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IAssetCountService countService)
        {
            _countService = countService;
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
                    CodePrefix = m.Category.CodePrefix ?? ""
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

            // Branch letter by id, so the register forms can show the full
            // code (M-LIB-CHR-0001) before saving.
            ViewBag.BranchCodes = await _context.Branches
                .ToDictionaryAsync(b => b.BranchID, b => b.BranchCode);

            ViewBag.LocationsByBranch = await _context.Locations
                .OrderBy(l => l.LocationName)
                .Select(l => new LocationDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    LocationCode = l.LocationCode,
                    Description = l.Description,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName
                })
                .ToListAsync();

            // The Property Department first: it is the usual choice (in stock).
            ViewBag.DepartmentsByBranch = await _context.Departments
                .OrderByDescending(d => d.IsProperty).ThenBy(d => d.DepartmentName)
                .Select(d => new DepartmentDTO
                {
                    DepartmentID = d.DepartmentID,
                    DepartmentName = d.DepartmentName,
                    Description = d.Description,
                    BranchID = d.BranchID,
                    BranchName = d.Branch.BranchName,
                    IsProperty = d.IsProperty
                })
                .ToListAsync();

            // For the "+ New model" popup's category list. Categories with
            // no code prefix are left out -- no asset could be registered
            // under them anyway.
            ViewBag.QuickAddCategories = await _context.Categories
                .Where(c => c.Kind == CategoryKind.Asset && c.CodePrefix != null && c.CodePrefix != "")
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
                .Where(c => c.Kind == CategoryKind.Asset)
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

            // The User filter: everyone active, plus anyone inactive who
            // still holds something (so their units can still be found).
            // Each carries their department and branch, which narrow the
            // list on the page.
            model.UserOptions = await _context.Users
                .Where(u => u.Status == "Active" || u.AssignedAssets.Any())
                .OrderBy(u => u.Branch.BranchName).ThenBy(u => u.Department.DepartmentName)
                .ThenBy(u => u.FirstName).ThenBy(u => u.LastName)
                .Select(u => new AssetFilterUserOption
                {
                    UserID = u.UserID,
                    FullName = u.FirstName + " " + u.LastName,
                    DepartmentID = u.DepartmentID,
                    DepartmentName = u.Department.DepartmentName,
                    BranchID = u.BranchID,
                    BranchName = u.Branch.BranchName,
                    IsActive = u.Status == "Active"
                })
                .ToListAsync();

            ViewBag.StatusFilter = new SelectList(
                Enum.GetValues(typeof(AssetStatus)).Cast<AssetStatus>()
                    .Select(s => new { Value = s.ToString(), Text = s == AssetStatus.UnderMaintenance ? "Maintenance" : DisplayText.AssetStatusLabel(s) }),
                "Value", "Text", model.Status?.ToString());

            ViewBag.ConditionFilter = new SelectList(
                Enum.GetValues(typeof(ConditionStatus)).Cast<ConditionStatus>()
                    .Select(c => new { Value = c.ToString(), Text = c.ToString() }),
                "Value", "Text", model.Condition?.ToString());
        }

        // The Items list's "Held by" filter: a person, a department (no
        // person), or nobody. Anything else leaves the list as it is.
        private static List<AssetResponseDTO> ByHolder(List<AssetResponseDTO> assets, string? holder) => holder switch
        {
            "person" => assets.Where(a => a.AssignedUserID != null).ToList(),
            "department" => assets.Where(a => a.IsWithDepartment).ToList(),
            "none" => assets.Where(a => a.AssignedUserID == null && !a.IsWithDepartment).ToList(),
            _ => assets
        };

        private static string? CleanHolder(string? holder) =>
            holder is "person" or "department" or "none" ? holder : null;

        // GET /Assets
        public async Task<IActionResult> Index(
            string? keyword, int? categoryId, int? departmentId,
            AssetStatus? status, ConditionStatus? condition, int? locationId, int? modelId,
            int? branchId, int? userId = null, bool includeDisposed = false, int page = 1, int? pageSize = null,
            string? holder = null)
        {
            holder = CleanHolder(holder);
            var size = pageSize.HasValue && PageSizeOptions.Contains(pageSize.Value)
                ? pageSize.Value
                : PageSize;

            if (IsRequesterOnly)
                return await CatalogAsync(keyword, categoryId, page, size);

            // One query drives both the table and the KPI tiles, which is
            // what makes the tiles mirror the rows: every filter, status
            // included, narrows this set, and the tiles are just its
            // breakdown. With a status selected the others read zero.
            var matches = ByHolder(await _assetService.SearchAssetsAsync(
                keyword, categoryId, modelId, branchId, departmentId, status, condition, locationId, userId), holder);

            // Disposed units are hidden unless asked for. Done here, not in
            // SearchAssetsAsync, because the request screens and barcode scan use
            // that too and must keep finding every unit. The Disposed tile
            // still counts them, so it says how many are hidden.
            var showDisposed = includeDisposed || status == AssetStatus.Disposed;
            var filtered = showDisposed
                ? matches
                : matches.Where(a => a.Status != AssetStatus.Disposed).ToList();

            var totalFiltered = filtered.Count;
            var lastPage = Math.Max(1, (int)Math.Ceiling(totalFiltered / (double)size));
            var currentPage = Math.Clamp(page, 1, lastPage);

            var paged = filtered
                .Skip((currentPage - 1) * size)
                .Take(size)
                .ToList();

            // Unfiltered headcount, for the "N of M" caption above the tiles.
            // A COUNT(*) rather than another GetAllAssetsAsync -- the page
            // needs the number, not the rows. Leaves out disposed units when
            // the list does, so the two numbers describe the same set.
            var grandTotal = showDisposed
                ? await _context.Assets.CountAsync()
                : await _context.Assets.CountAsync(a => a.Status != AssetStatus.Disposed);
            // For the Assets / Supplies switch: never counts disposed units.
            ViewBag.AssetTotal = showDisposed
                ? await _context.Assets.CountAsync(a => a.Status != AssetStatus.Disposed)
                : grandTotal;

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
                UserId = userId,
                Holder = holder,
                Page = currentPage,
                PageSize = size,
                PageSizeOptions = PageSizeOptions,
                TotalFilteredCount = totalFiltered
            };

            await PopulateFilterOptionsAsync(model);
            // For the Inventory menu.
            ViewBag.RedTagCount = await _context.Assets.CountAsync(a => a.Status == AssetStatus.RedTagged);

            return View(model);
        }

        // GET /Assets/ExportAssets -- same filters as Index, no paging.
        public async Task<IActionResult> ExportAssets(
            string? keyword, int? categoryId, int? departmentId,
            AssetStatus? status, ConditionStatus? condition, int? locationId, int? modelId,
            int? branchId, int? userId = null, bool includeDisposed = false, string? holder = null)
        {
            if (IsRequesterOnly)
                return Forbid();

            var assets = ByHolder(await _assetService.SearchAssetsAsync(
                keyword, categoryId, modelId, branchId, departmentId, status, condition, locationId, userId), CleanHolder(holder));

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
                    a.AssignedUserName ?? (a.IsWithDepartment ? a.DepartmentName + " (department)" : null),
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
            // Everything the department has, shared by its members.
            return View(await _assignmentService.GetDepartmentHoldingsAsync(CurrentUserId));
        }

        // GET /Assets/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            if (!CanOpenUnit(asset) && !await _assignmentService.IsDepartmentAssetAsync(CurrentUserId, id))
            {
                TempData["ErrorMessage"] = "You can only open items your department has. See My Assets.";
                return RedirectToAction(nameof(Index));
            }

            // The last physical count it was on, for the side panel.
            ViewBag.LastCount = await _countService.GetLastCountForAssetAsync(id);
            return View(asset);
        }

        // The Assets page for Teachers and Staff: every model, with its
        // category, description and how many the Property Department has
        // in stock to lend. They request a model; the approver picks the
        // unit. A model with none in stock can only be asked to be bought.
        private async Task<IActionResult> CatalogAsync(string? keyword, int? categoryId, int page, int pageSize)
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

            var total = await query.CountAsync();
            // For the Assets / Supplies switch.
            ViewBag.AssetTotal = await _context.Assets.CountAsync(a => a.Status != AssetStatus.Disposed);
            var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            var currentPage = Math.Clamp(page, 1, lastPage);

            var model = new AssetCatalogViewModel
            {
                Keyword = keyword,
                CategoryId = categoryId,
                Page = currentPage,
                PageSize = pageSize,
                PageSizeOptions = PageSizeOptions,
                TotalCount = total,
                Models = await query
                    .OrderBy(m => m.Category.CategoryName).ThenBy(m => m.ModelName)
                    .Skip((currentPage - 1) * pageSize)
                    .Take(pageSize)
                    .Select(m => new ModelDTO
                    {
                        ModelID = m.ModelID,
                        ModelName = m.ModelName,
                        CategoryID = m.CategoryID,
                        CategoryName = m.Category.CategoryName,
                        Description = m.Description,
                        AvailableCount = m.Assets.Count(a => a.Status == AssetStatus.Available)
                    })
                    .ToListAsync(),
                CategoryOptions = await _context.Categories
                    .Where(c => c.Kind == CategoryKind.Asset)
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
        [OneTimeSubmit]
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
                    CreateBarcode = model.CreateBarcode,
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
        // ?newItemRequestId= registers the units bought for an arrived
        // new-item request: the first line still short of units is filled
        // in, in the requesting department's branch. The units are held by
        // the Property Department for that request until the requester
        // confirms receipt.
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> BulkCreate(int? newItemRequestId)
        {
            var model = new AssetBulkCreateViewModel();

            if (newItemRequestId is int ticketId)
            {
                var ticket = await LoadBulkTicketAsync(ticketId);
                if (ticket is null)
                    return RedirectToAction("Details", "NewItemRequests", new { id = ticketId });

                if (ticket.Registered >= ticket.Ordered)
                {
                    TempData["StatusMessage"] = $"All {ticket.Ordered} units of request #{ticketId} are registered. Mark it delivered when they are brought over.";
                    return RedirectToAction("Details", "NewItemRequests", new { id = ticketId });
                }

                var line = ticket.Lines.FirstOrDefault(l => l.ModelID != null && l.Registered < l.Quantity)
                           ?? ticket.Lines.FirstOrDefault(l => l.ModelID == null)
                           ?? ticket.Lines.First();
                var left = ticket.Ordered - ticket.Registered;

                model.NewItemRequestID = ticketId;
                model.ModelID = line.ModelID;
                model.Quantity = Math.Min(left, line.ModelID is null ? left : Math.Max(1, line.Quantity - line.Registered));
                model.BaseName = line.ItemName;
                model.AcquisitionCost = line.EstimatedUnitPrice;
                model.AcquisitionDate = PhilippineTime.Today;
                model.BranchID = ticket.BranchID;
                model.DepartmentID = ticket.PropertyDepartmentID;
                ViewBag.Ticket = ticket;
            }

            await PopulateDropdownsAsync();
            return View(model);
        }

        // The ticket a bulk registration is for, or null (with the reason in
        // TempData) when it cannot take units now.
        private async Task<BulkTicketInfo?> LoadBulkTicketAsync(int ticketId)
        {
            var ticket = await _context.NewItemRequests
                .Where(n => n.NewItemRequestID == ticketId)
                .Select(n => new
                {
                    n.RequestStatus,
                    n.ItemKind,
                    Info = new BulkTicketInfo
                    {
                        RequestID = n.NewItemRequestID,
                        RequesterName = n.RequestedByUser.FirstName + " " + n.RequestedByUser.LastName,
                        DepartmentName = n.Department.DepartmentName,
                        BranchID = n.Department.BranchID,
                        BranchName = n.Department.Branch.BranchName,
                        Registered = n.RegisteredAssets.Count()
                    },
                    Lines = n.Items.OrderBy(i => i.NewItemRequestItemID).Select(i => new BulkTicketLine
                    {
                        ItemName = i.ItemName,
                        ModelID = i.ModelID,
                        Quantity = i.Quantity,
                        EstimatedUnitPrice = i.EstimatedUnitPrice
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (ticket is null)
            {
                TempData["ErrorMessage"] = "New-item request not found.";
                return null;
            }
            if (ticket.ItemKind != CategoryKind.Asset)
            {
                TempData["ErrorMessage"] = "That request is for supplies; add them to supplies stock instead.";
                return null;
            }
            if (ticket.RequestStatus != NewItemStatus.Arrived)
            {
                TempData["ErrorMessage"] = "Units are registered for a request once it has arrived and before it is delivered.";
                return null;
            }

            var info = ticket.Info;
            info.Lines = ticket.Lines;
            info.Ordered = info.Lines.Sum(l => l.Quantity);

            var byModel = await _context.Assets
                .Where(a => a.NewItemRequestID == ticketId)
                .GroupBy(a => a.ModelID)
                .Select(g => new { ModelID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ModelID, x => x.Count);
            foreach (var line in info.Lines.Where(l => l.ModelID != null))
                line.Registered = byModel.GetValueOrDefault(line.ModelID!.Value);

            info.PropertyDepartmentID = await _context.Departments
                .Where(d => d.BranchID == info.BranchID && d.IsProperty)
                .Select(d => d.DepartmentID)
                .FirstOrDefaultAsync();
            return info;
        }

        // POST /Assets/BulkCreate
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        [RequestSizeLimit(FileUploads.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = FileUploads.MaxRequestBytes)]
        [OneTimeSubmit]
        public async Task<IActionResult> BulkCreate(AssetBulkCreateViewModel model)
        {
            if (model.NewItemRequestID is int forTicket)
            {
                var ticket = await LoadBulkTicketAsync(forTicket);
                if (ticket is null)
                    return RedirectToAction("Details", "NewItemRequests", new { id = forTicket });
                ViewBag.Ticket = ticket;
            }

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
                    CreateBarcode = model.CreateBarcode,
                    NewItemRequestID = model.NewItemRequestID
                };

                var codes = await _assetService.BulkCreateAssetsAsync(dto, CurrentUserId);

                // For a request: back to it, so the next line or the
                // delivery is right there.
                if (model.NewItemRequestID is int ticketId)
                {
                    TempData["StatusMessage"] = (codes.Count == 1
                        ? $"Registered 1 unit for request #{ticketId}: {codes[0]}."
                        : $"Registered {codes.Count} units for request #{ticketId}: {codes[0]} to {codes[^1]}.")
                        + " They are held by the Property Department until the requester confirms receipt.";
                    return RedirectToAction("Details", "NewItemRequests", new { id = ticketId });
                }

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
            if ((asset.Status == AssetStatus.Assigned && !asset.IsWithDepartment) || asset.Status == AssetStatus.InTransit ||
                asset.Status == AssetStatus.Overdue)
            {
                TempData["ErrorMessage"] = "This asset is out on a request and cannot be edited until it's returned.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await PopulateDropdownsAsync();
            await SetFormHeaderAsync(asset);

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
            await SetFormHeaderAsync(current);

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

        // The Edit form's asset strip and its "Last edited ... by ..." line,
        // from the newest audit entry for the asset.
        private async Task SetFormHeaderAsync(AssetResponseDTO asset)
        {
            ViewBag.Asset = asset;
            ViewBag.LastEdit = await _context.AuditLogs
                .Where(a => a.TargetAssetID == asset.AssetID)
                .OrderByDescending(a => a.LogDateTime)
                .Select(a => new { a.LogDateTime, Name = a.User.FirstName + " " + a.User.LastName })
                .FirstOrDefaultAsync() is { } last
                ? $"Last edited {last.LogDateTime:MMM d, yyyy} by {last.Name}"
                : null;
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

            // Only a red-tagged unit is disposed of; its reason carries over.
            if (asset.Status != AssetStatus.RedTagged)
            {
                TempData["ErrorMessage"] = "Red-tag it first. Only red-tagged assets can be disposed of.";
                return RedirectToAction(nameof(Details), new { id });
            }
            model.ReasonForDisposal = asset.RedTagReason ?? "";

            ViewBag.Asset = asset;
            return View(model);
        }

        // GET /Assets/RedTags
        // Every red-tagged unit, oldest tag first.
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> RedTags()
        {
            return View(await _disposalService.GetRedTaggedAsync());
        }

        // GET /Assets/RedTag/5
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> RedTag(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            if (!asset.CanBeRedTagged)
            {
                TempData["ErrorMessage"] = asset.Status == AssetStatus.RedTagged
                    ? "This asset is already red tagged."
                    : "This asset is out on a request or with a person. Record its return first, then red-tag it.";
                return RedirectToAction(nameof(Details), new { id });
            }

            ViewBag.Asset = asset;
            return View(new RedTagViewModel
            {
                AssetID = asset.AssetID,
                AssetRowVersionBase64 = RowVersionHelper.ToBase64(asset.RowVersion)
            });
        }

        // POST /Assets/RedTag/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> RedTag(int id, RedTagViewModel model)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();
            ViewBag.Asset = asset;

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _disposalService.RedTagAsync(id, model.Reason,
                    RowVersionHelper.FromBase64(model.AssetRowVersionBase64), CurrentUserId);
                TempData["StatusMessage"] = $"{asset.AssetCode} is red tagged for disposal.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(model);
            }
        }

        // ---- Import items from Excel ----------------------------------------
        // GET /Assets/Import : the template, and the upload.
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public IActionResult Import() => View();

        // GET /Assets/ImportTemplate : the Excel template, with today's lists.
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> ImportTemplate([FromServices] ItemImport import) =>
            File(await import.TemplateAsync(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Property import template.xlsx");

        // POST /Assets/Import : checks the file and shows every row. Kept on
        // the server for "Import" (ImportSave) when nothing is wrong.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(10_000_000)]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Import(IFormFile? file, [FromServices] ItemImport import)
        {
            if (file is null || file.Length == 0 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Pick the filled-in template (an .xlsx file).";
                return RedirectToAction(nameof(Import));
            }
            var token = Guid.NewGuid().ToString("N");
            var path = Path.Combine(Path.GetTempPath(), $"sab-import-{token}.xlsx");
            await using (var save = System.IO.File.Create(path))
                await file.CopyToAsync(save);
            try
            {
                await using var read = System.IO.File.OpenRead(path);
                ViewBag.Rows = await import.CheckAsync(read);
            }
            catch (Exception)
            {
                System.IO.File.Delete(path);
                TempData["ErrorMessage"] = "That file could not be read as Excel. Download the template, fill it in and upload it again.";
                return RedirectToAction(nameof(Import));
            }
            ViewBag.Token = token;
            ViewBag.FileName = file.FileName;
            return View();
        }

        // POST /Assets/ImportSave : imports a checked file.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> ImportSave(string token, [FromServices] ItemImport import)
        {
            if (string.IsNullOrEmpty(token) || token.Any(ch => !Uri.IsHexDigit(ch)))
                return RedirectToAction(nameof(Import));
            var path = Path.Combine(Path.GetTempPath(), $"sab-import-{token}.xlsx");
            if (!System.IO.File.Exists(path))
            {
                TempData["ErrorMessage"] = "That upload has expired. Upload the file again.";
                return RedirectToAction(nameof(Import));
            }
            try
            {
                List<ItemImport.Row> rows;
                await using (var read = System.IO.File.OpenRead(path))
                    rows = await import.CheckAsync(read);
                if (rows.Any(r => r.Problems.Count > 0))
                {
                    TempData["ErrorMessage"] = "Some rows have problems now. Fix the file and upload it again.";
                    return RedirectToAction(nameof(Import));
                }
                var (done, failed) = await import.ImportAsync(rows, CurrentUserId);
                TempData["StatusMessage"] = $"Imported {done.Count} row{(done.Count == 1 ? "" : "s")} into the Property Department's stock.";
                if (failed.Count > 0)
                    TempData["ErrorMessage"] = "Not imported: " + string.Join(" · ", failed);
                return RedirectToAction(nameof(Index));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        // POST /Assets/RedTagMany : red tag mode on Property -- the ticked
        // units, one reason for all.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> RedTagMany(List<int> assetIds, string? reason)
        {
            reason = reason?.Trim();
            if (assetIds is null || assetIds.Count == 0 || string.IsNullOrEmpty(reason))
            {
                TempData["ErrorMessage"] = "Tick at least one unit and say why they are red tagged.";
                return RedirectToAction(nameof(Index));
            }

            var done = new List<string>();
            var failed = new List<string>();
            foreach (var id in assetIds.Distinct())
            {
                var asset = await _assetService.GetAssetByIdAsync(id);
                if (asset is null) continue;
                try
                {
                    await _disposalService.RedTagAsync(id, reason, asset.RowVersion, CurrentUserId);
                    done.Add(asset.AssetCode);
                }
                catch (Exception ex)
                {
                    failed.Add($"{asset.AssetCode}: {UserMessageFor(ex)}");
                }
            }
            if (done.Count > 0)
                TempData["StatusMessage"] = $"{done.Count} unit{(done.Count == 1 ? "" : "s")} red tagged for disposal: {string.Join(", ", done)}.";
            if (failed.Count > 0)
                TempData["ErrorMessage"] = "Not red tagged: " + string.Join(" · ", failed);
            return RedirectToAction(nameof(Index));
        }

        // POST /Assets/RemoveRedTag/5 -- back into Property stock.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> RemoveRedTag(int id, string rowVersionBase64)
        {
            try
            {
                await _disposalService.RemoveRedTagAsync(id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);
                TempData["StatusMessage"] = "Red tag removed. The asset is back in Property stock.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST /Assets/Dispose/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Dispose(int id, DisposeAssetViewModel model)
        {
            // For the asset strip at the top if the form is shown again.
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();
            ViewBag.Asset = asset;

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
                    .Include(a => a.User)
                        .ThenInclude(u => u.Role)
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

        // GET /Assets/Barcode/5 -- draws the Code 128 label as SVG on demand
        // from the code stored when the barcode was created. Nothing stored
        // means no barcode. ?download=true sends it as a file to print.
        public async Task<IActionResult> Barcode(int id, bool download = false)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null || string.IsNullOrEmpty(asset.BarcodeData) || !CanOpenUnit(asset))
                return NotFound();

            var svg = Encoding.UTF8.GetBytes(Code128.Svg(asset.BarcodeData));
            return download
                ? File(svg, "image/svg+xml", $"{asset.BarcodeData}.svg")
                : File(svg, "image/svg+xml");
        }

        // POST /Assets/CreateBarcode/5 -- the Details page's "Create barcode"
        // button, for an asset that was registered without one.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> CreateBarcode(int id)
        {
            try
            {
                await _assetService.CreateBarcodeAsync(id, CurrentUserId);
                TempData["StatusMessage"] = "Barcode created.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST /Assets/DeleteBarcode/5 -- the Details page's "Delete barcode"
        // button. The page asks "are you sure?" before posting.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> DeleteBarcode(int id)
        {
            try
            {
                await _assetService.DeleteBarcodeAsync(id, CurrentUserId);
                TempData["StatusMessage"] = "Barcode deleted.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // GET /Assets/Scan?code=M-LIB-CHR-0001
        //
        // The Scan box on the Assets page. A USB barcode scanner types the
        // barcode into it and presses Enter, which submits here. Typing the
        // asset code (CHR-0001) works too, but only for an asset whose
        // barcode was created. A deleted barcode's old label finds nothing.
        public async Task<IActionResult> Scan(string? code)
        {
            code = code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                TempData["ErrorMessage"] = "Scan a barcode or type an asset code.";
                return RedirectToAction(nameof(Index));
            }

            var match = await _context.Assets
                .Where(a => a.BarcodeData == code || a.AssetCode == code)
                .Select(a => new { a.AssetID, a.AssetCode, a.BarcodeData })
                .FirstOrDefaultAsync();

            if (match is null)
            {
                TempData["ErrorMessage"] = $"No asset found for '{code}'.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrEmpty(match.BarcodeData))
            {
                TempData["ErrorMessage"] = $"{match.AssetCode} has no barcode. Open the asset and click Create barcode to make one.";
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Details), new { id = match.AssetID });
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