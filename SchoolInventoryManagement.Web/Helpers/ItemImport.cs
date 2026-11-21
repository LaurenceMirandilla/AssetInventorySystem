using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Import items from Excel (Property > Import items): many fixed assets
    // and consumables at once, into the Property Department's stock of each
    // row's branch. The template has a sheet for each, and a Lists sheet with
    // what the dropdowns offer. Every row is checked first (Check); nothing
    // is saved while any row has a problem. Import then registers the units
    // (a new model is added under its category) and adds the consumables
    // (a new supply is added to the list), through the usual services.
    public class ItemImport
    {
        public const string AssetSheet = "Fixed assets";
        public const string SupplySheet = "Consumables";
        public const string ListSheet = "Lists";

        static readonly string[] AssetHeaders =
            { "Category", "Model", "Quantity", "Branch", "Room", "Condition", "Serial numbers", "Cost each", "Acquired on", "Description" };
        static readonly string[] SupplyHeaders =
            { "Supply", "Category", "Unit", "Quantity", "Branch", "Room", "Low stock level", "Description" };

        private readonly ApplicationDbContext _context;
        private readonly IAssetService _assets;
        private readonly IModelService _models;
        private readonly ISupplyService _supplies;

        public ItemImport(ApplicationDbContext context, IAssetService assets, IModelService models, ISupplyService supplies)
        {
            _context = context;
            _assets = assets;
            _models = models;
            _supplies = supplies;
        }

        // One row of the file, checked.
        public class Row
        {
            public string Sheet { get; set; } = "";
            public int Number { get; set; }
            public string What { get; set; } = "";
            public List<string> Problems { get; set; } = new();

            // Resolved values.
            public int? CategoryID { get; set; }
            public int? ModelID { get; set; }
            public string Name { get; set; } = "";
            public int Quantity { get; set; }
            public int BranchID { get; set; }
            public int LocationID { get; set; }
            public int PropertyDepartmentID { get; set; }
            public ConditionStatus Condition { get; set; } = ConditionStatus.Good;
            public string? Serials { get; set; }
            public decimal? Cost { get; set; }
            public DateTime? AcquiredOn { get; set; }
            public string? Description { get; set; }
            public int? SupplyID { get; set; }
            public string? Unit { get; set; }
            public int LowStockLevel { get; set; }
        }

        private class Lookups
        {
            public List<(int Id, string Name, CategoryKind Kind)> Categories = new();
            public List<(int Id, string Name, int CategoryID)> Models = new();
            public List<(int Id, string Name)> Branches = new();
            public List<(int Id, string Name, int BranchID)> Rooms = new();
            public Dictionary<int, int> PropertyByBranch = new();
            public List<(int Id, string Name, string Unit)> Supplies = new();
        }

        private async Task<Lookups> LoadAsync() => new Lookups
        {
            Categories = (await _context.Categories.AsNoTracking().Select(c => new { c.CategoryID, c.CategoryName, c.Kind }).ToListAsync())
                .Select(c => (c.CategoryID, c.CategoryName.Trim(), c.Kind)).ToList(),
            Models = (await _context.Models.AsNoTracking().Select(m => new { m.ModelID, m.ModelName, m.CategoryID }).ToListAsync())
                .Select(m => (m.ModelID, m.ModelName.Trim(), m.CategoryID)).ToList(),
            Branches = (await _context.Branches.AsNoTracking().Select(b => new { b.BranchID, b.BranchName }).ToListAsync())
                .Select(b => (b.BranchID, b.BranchName.Trim())).ToList(),
            Rooms = (await _context.Locations.AsNoTracking().Select(l => new { l.LocationID, l.LocationName, l.BranchID }).ToListAsync())
                .Select(l => (l.LocationID, l.LocationName.Trim(), l.BranchID)).ToList(),
            PropertyByBranch = await _context.Departments.AsNoTracking().Where(d => d.IsProperty)
                .ToDictionaryAsync(d => d.BranchID, d => d.DepartmentID),
            Supplies = (await _context.Supplies.AsNoTracking().Select(s => new { s.SupplyID, s.SupplyName, s.Unit }).ToListAsync())
                .Select(s => (s.SupplyID, s.SupplyName.Trim(), s.Unit)).ToList()
        };

        static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        // ---- The template ----------------------------------------------------
        public async Task<byte[]> TemplateAsync()
        {
            var l = await LoadAsync();
            using var wb = new XLWorkbook();
            var assets = wb.Worksheets.Add(AssetSheet);
            var supplies = wb.Worksheets.Add(SupplySheet);
            var lists = wb.Worksheets.Add(ListSheet);

            void Head(IXLWorksheet ws, string[] heads)
            {
                for (var i = 0; i < heads.Length; i++)
                    ws.Cell(1, i + 1).Value = heads[i];
                var r = ws.Range(1, 1, 1, heads.Length);
                r.Style.Font.Bold = true;
                r.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E8B47");
                r.Style.Font.FontColor = XLColor.White;
                ws.SheetView.FreezeRows(1);
                ws.Columns(1, heads.Length).Width = 22;
            }
            Head(assets, AssetHeaders);
            Head(supplies, SupplyHeaders);

            // The Lists sheet: what the dropdowns offer, and how to fill it in.
            void Column(int col, string title, IEnumerable<string> values)
            {
                lists.Cell(1, col).Value = title;
                lists.Cell(1, col).Style.Font.Bold = true;
                var row = 2;
                foreach (var v in values) lists.Cell(row++, col).Value = v;
            }
            var assetCats = l.Categories.Where(c => c.Kind == CategoryKind.Asset).Select(c => c.Name).OrderBy(n => n).ToList();
            var supplyCats = l.Categories.Where(c => c.Kind == CategoryKind.Supply).Select(c => c.Name).OrderBy(n => n).ToList();
            var branchNames = l.Branches.Select(b => b.Name).OrderBy(n => n).ToList();
            var roomNames = l.Rooms.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            var conditions = Enum.GetNames<ConditionStatus>().ToList();
            Column(1, "Fixed asset categories", assetCats);
            Column(2, "Models (or type a new one)", l.Models.Select(m => m.Name).OrderBy(n => n));
            Column(3, "Consumable categories", supplyCats);
            Column(4, "Consumables (or type a new one)", l.Supplies.Select(s => s.Name).OrderBy(n => n));
            Column(5, "Branches", branchNames);
            Column(6, "Rooms", roomNames);
            Column(7, "Conditions", conditions);
            lists.Cell(1, 9).Value = "How to fill it in";
            lists.Cell(1, 9).Style.Font.Bold = true;
            var help = new[]
            {
                "One row per item. Everything goes into the Property Department's stock of the row's branch.",
                "Fixed assets: Category, Model, Quantity, Branch and Room are needed. A model not on the list is added under its category.",
                "Serial numbers: one per unit, separated by commas, in the order the units are numbered (optional).",
                "Condition: one of the Conditions column (Good when left empty). Cost each: a number. Acquired on: a date.",
                "Consumables: Supply, Category, Unit, Quantity, Branch and Room are needed. A supply on the list is added to its stock; a new one is added to the list.",
                "Low stock level: when to warn that it runs low (new supplies only).",
                "Upload the file on Property > Import items. Every row is checked first; nothing is saved while any row has a problem."
            };
            for (var i = 0; i < help.Length; i++) lists.Cell(i + 2, 9).Value = help[i];
            lists.Column(9).Width = 110;
            lists.Columns(1, 7).Width = 26;

            // Dropdowns on the two sheets.
            static string Ref(int col, int count) => $"'{ListSheet}'!${(char)('A' + col - 1)}$2:${(char)('A' + col - 1)}${Math.Max(2, count + 1)}";
            void Pick(IXLWorksheet ws, int col, int listCol, int count, bool strict)
            {
                var dv = ws.Range(2, col, 1000, col).CreateDataValidation();
                dv.List(Ref(listCol, count), true);
                dv.IgnoreBlanks = true;
                if (!strict) dv.ShowErrorMessage = false;
            }
            Pick(assets, 1, 1, assetCats.Count, true);
            Pick(assets, 2, 2, l.Models.Count, false);
            Pick(assets, 4, 5, branchNames.Count, true);
            Pick(assets, 5, 6, roomNames.Count, true);
            Pick(assets, 6, 7, conditions.Count, true);
            Pick(supplies, 1, 4, l.Supplies.Count, false);
            Pick(supplies, 2, 3, supplyCats.Count, true);
            Pick(supplies, 5, 5, branchNames.Count, true);
            Pick(supplies, 6, 6, roomNames.Count, true);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ---- Checking a file --------------------------------------------------
        public async Task<List<Row>> CheckAsync(Stream file)
        {
            var l = await LoadAsync();
            var rows = new List<Row>();
            using var wb = new XLWorkbook(file);

            string Text(IXLRow r, int c) => r.Cell(c).GetFormattedString().Trim();
            int? Int(IXLRow r, int c)
            {
                var t = Text(r, c);
                return t.Length == 0 ? null : int.TryParse(t.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
            }
            void Place(Row row, string branch, string room)
            {
                var b = l.Branches.FirstOrDefault(x => Same(x.Name, branch));
                if (branch.Length == 0) row.Problems.Add("Branch is empty.");
                else if (b.Id == 0) row.Problems.Add($"No branch called \"{branch}\".");
                else
                {
                    row.BranchID = b.Id;
                    if (l.PropertyByBranch.TryGetValue(b.Id, out var p)) row.PropertyDepartmentID = p;
                    else row.Problems.Add($"{b.Name} has no Property Department yet.");
                    var loc = l.Rooms.FirstOrDefault(x => x.BranchID == b.Id && Same(x.Name, room));
                    if (room.Length == 0) row.Problems.Add("Room is empty.");
                    else if (loc.Id == 0) row.Problems.Add($"No room called \"{room}\" in {b.Name}.");
                    else row.LocationID = loc.Id;
                }
            }
            void Qty(Row row, int? q)
            {
                if (q is null) row.Problems.Add("Quantity is empty.");
                else if (q < 1 || q > (row.Sheet == AssetSheet ? 200 : 1000000)) row.Problems.Add($"Quantity must be a whole number from 1 to {(row.Sheet == AssetSheet ? "200" : "1,000,000")}.");
                else row.Quantity = q.Value;
            }

            if (wb.TryGetWorksheet(AssetSheet, out var aws))
            {
                foreach (var r in aws.RowsUsed().Where(r => r.RowNumber() > 1))
                {
                    if (Enumerable.Range(1, AssetHeaders.Length).All(c => Text(r, c).Length == 0)) continue;
                    var row = new Row { Sheet = AssetSheet, Number = r.RowNumber() };
                    var cat = Text(r, 1); var model = Text(r, 2);
                    row.Name = model;
                    var c = l.Categories.FirstOrDefault(x => x.Kind == CategoryKind.Asset && Same(x.Name, cat));
                    if (cat.Length == 0) row.Problems.Add("Category is empty.");
                    else if (c.Id == 0) row.Problems.Add($"No fixed asset category called \"{cat}\". Add it under Categories first.");
                    else row.CategoryID = c.Id;
                    if (model.Length == 0) row.Problems.Add("Model is empty.");
                    else if (model.Length > 150) row.Problems.Add("The model name can be at most 150 characters.");
                    else
                    {
                        var m = l.Models.FirstOrDefault(x => Same(x.Name, model));
                        if (m.Id != 0)
                        {
                            if (row.CategoryID is int cid && m.CategoryID != cid)
                                row.Problems.Add($"\"{m.Name}\" is already a model in another category.");
                            row.ModelID = m.Id;
                            row.Name = m.Name;
                        }
                    }
                    Qty(row, Int(r, 3));
                    Place(row, Text(r, 4), Text(r, 5));
                    var cond = Text(r, 6);
                    if (cond.Length > 0)
                    {
                        if (Enum.TryParse<ConditionStatus>(cond, true, out var cs)) row.Condition = cs;
                        else row.Problems.Add($"Condition \"{cond}\" is not one of {string.Join(", ", Enum.GetNames<ConditionStatus>())}.");
                    }
                    var serials = Text(r, 7);
                    if (serials.Length > 0)
                    {
                        var list = serials.Split(',').Select(s => s.Trim()).ToList();
                        if (list.Count > row.Quantity && row.Quantity > 0) row.Problems.Add($"{list.Count} serial numbers for {row.Quantity} units.");
                        row.Serials = string.Join("\n", list);
                    }
                    var cost = Text(r, 8);
                    if (cost.Length > 0)
                    {
                        if (decimal.TryParse(cost.Replace(",", "").Replace("₱", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0) row.Cost = d;
                        else row.Problems.Add($"Cost each \"{cost}\" is not a number.");
                    }
                    var on = r.Cell(9);
                    if (!on.IsEmpty())
                    {
                        if (on.DataType == XLDataType.DateTime) row.AcquiredOn = on.GetDateTime().Date;
                        else if (DateTime.TryParse(on.GetFormattedString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) row.AcquiredOn = dt.Date;
                        else row.Problems.Add($"Acquired on \"{on.GetFormattedString()}\" is not a date.");
                    }
                    row.Description = Text(r, 10) is { Length: > 0 } desc ? desc : null;
                    row.What = $"{row.Quantity} × {row.Name}" + (row.ModelID is null && row.Name.Length > 0 ? " (new model)" : "");
                    rows.Add(row);
                }
            }

            if (wb.TryGetWorksheet(SupplySheet, out var sws))
            {
                foreach (var r in sws.RowsUsed().Where(r => r.RowNumber() > 1))
                {
                    if (Enumerable.Range(1, SupplyHeaders.Length).All(c => Text(r, c).Length == 0)) continue;
                    var row = new Row { Sheet = SupplySheet, Number = r.RowNumber() };
                    var name = Text(r, 1); var cat = Text(r, 2); var unit = Text(r, 3);
                    row.Name = name;
                    if (name.Length == 0) row.Problems.Add("Supply is empty.");
                    else if (name.Length > 150) row.Problems.Add("The supply name can be at most 150 characters.");
                    var s = l.Supplies.FirstOrDefault(x => Same(x.Name, name));
                    if (s.Id != 0) { row.SupplyID = s.Id; row.Name = s.Name; row.Unit = s.Unit; }
                    var c = l.Categories.FirstOrDefault(x => x.Kind == CategoryKind.Supply && Same(x.Name, cat));
                    if (s.Id == 0)
                    {
                        if (cat.Length == 0) row.Problems.Add("Category is empty.");
                        else if (c.Id == 0) row.Problems.Add($"No consumable category called \"{cat}\". Add it under Categories first.");
                        else row.CategoryID = c.Id;
                        if (unit.Length == 0) row.Problems.Add("Unit is empty (box, ream, piece...).");
                        else if (unit.Length > 30) row.Problems.Add("The unit can be at most 30 characters.");
                        else row.Unit = unit;
                    }
                    Qty(row, Int(r, 4));
                    Place(row, Text(r, 5), Text(r, 6));
                    var low = Int(r, 7);
                    if (low is < 0) row.Problems.Add("Low stock level must be a whole number from 0.");
                    else row.LowStockLevel = low ?? 0;
                    row.Description = Text(r, 8) is { Length: > 0 } desc ? desc : null;
                    row.What = $"{row.Quantity} {row.Unit} {row.Name}" + (row.SupplyID is null && row.Name.Length > 0 ? " (new supply)" : "");
                    rows.Add(row);
                }
            }

            if (rows.Count == 0)
                rows.Add(new Row { Sheet = "File", Number = 0, What = "Nothing to import", Problems = { $"No rows found. Use the template: sheets \"{AssetSheet}\" and \"{SupplySheet}\", one item per row under the headings." } });
            return rows;
        }

        // ---- Importing a checked file -----------------------------------------
        // Returns a line per row saved, and the problems of any that failed.
        public async Task<(List<string> Done, List<string> Failed)> ImportAsync(List<Row> rows, int actingUserId)
        {
            var done = new List<string>();
            var failed = new List<string>();
            var newModels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var newSupplies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                try
                {
                    if (row.Sheet == AssetSheet)
                    {
                        var modelId = row.ModelID;
                        if (modelId is null && !newModels.TryGetValue(row.Name, out var made))
                        {
                            made = (await _models.CreateModelAsync(new CreateModelDTO { CategoryID = row.CategoryID!.Value, ModelName = row.Name }, actingUserId)).ModelID;
                            newModels[row.Name] = made;
                        }
                        modelId ??= newModels[row.Name];
                        var codes = await _assets.BulkCreateAssetsAsync(new BulkCreateAssetsDTO
                        {
                            ModelID = modelId.Value,
                            Quantity = row.Quantity,
                            BaseName = row.Name,
                            SerialNumbers = row.Serials,
                            Description = row.Description,
                            AcquisitionDate = row.AcquiredOn,
                            AcquisitionCost = row.Cost,
                            Condition = row.Condition,
                            BranchID = row.BranchID,
                            CurrentLocationID = row.LocationID,
                            DepartmentID = row.PropertyDepartmentID
                        }, actingUserId);
                        done.Add($"{row.Sheet} row {row.Number}: {row.Quantity} × {row.Name}" + (codes.Count > 0 ? $" ({codes.First()}{(codes.Count > 1 ? " to " + codes.Last() : "")})" : ""));
                    }
                    else
                    {
                        var supplyId = row.SupplyID ?? (newSupplies.TryGetValue(row.Name, out var s) ? s : (int?)null);
                        if (supplyId is null)
                        {
                            var id = await _supplies.CreateSupplyAsync(new CreateSupplyDTO
                            {
                                SupplyName = row.Name,
                                CategoryID = row.CategoryID,
                                Unit = row.Unit!,
                                Description = row.Description,
                                OpeningQuantity = row.Quantity,
                                LocationID = row.LocationID,
                                LowStockLevel = row.LowStockLevel
                                // No department: the Property Department of the room's branch.
                            }, actingUserId);
                            newSupplies[row.Name] = id;
                        }
                        else
                        {
                            await _supplies.AddStockAsync(new AddStockDTO
                            {
                                Source = "Other",
                                SourceName = "Imported from Excel",
                                Lines = new() { new AddStockLineDTO { SupplyID = supplyId, Quantity = row.Quantity, LocationID = row.LocationID } }
                            }, actingUserId);
                        }
                        done.Add($"{row.Sheet} row {row.Number}: {row.Quantity} {row.Unit} {row.Name}");
                    }
                }
                catch (Exception ex)
                {
                    failed.Add($"{row.Sheet} row {row.Number} ({row.Name}): {ex.Message}");
                }
            }
            return (done, failed);
        }
    }
}
